using System.Windows;
using System.Windows.Threading;
using AUTZ.IronWorks.Preview.Core;
using AUTZ.IronWorks.Preview.Models;
using AUTZ.IronWorks.Preview.Services;
using AUTZ.IronWorks.Preview.Views;

namespace AUTZ.IronWorks.Preview;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private PreviewManager? _manager;
    private NotifyIconService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) Log.TraceException("AppDomain unhandled exception", ex);
            else Log.Trace("EXCEPT", "AppDomain unhandled exception: " + args.ExceptionObject);
        };
        TaskScheduler.UnobservedTaskException += (_, args) => Log.TraceException("Unobserved task exception", args.Exception);

        // a bug in one handler should never take the whole app (and every thumbnail) down: log it and carry on
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write("Unhandled error: " + args.Exception);
            args.Handled = true;
        };

        WaitForPreviousInstance(e.Args);               // "Restart as administrator": let the old copy finish closing first
        // "Always start as administrator": a normal start hands over to the scheduled task (no Windows prompt) and closes itself.
        // A copy the task started, or one that was just restarted as administrator, never hands over again.
        // The task is only used when it starts this exe: a task left pointing at an old copy (for example the portable exe that was
        // deleted when the installer arrived) "starts" fine but runs nothing, so the app looked like it never opened.
        if (ProfileStore.LoadState().AlwaysAdmin && !MemoryCleaner.IsAdministrator && !HasArg(e.Args, AdminTaskService.TaskArgument) && !HasArg(e.Args, "--after-pid"))
        {
            if (!AdminTaskService.PointsAtThisExe(out string taskExe))
            {
                Log.Write("Always start as administrator: the scheduled task " + (taskExe.Length == 0 ? "is missing" : "points at a different copy of the app") +
                          ", so the app starts normally. Restart as administrator once to fix it.");
            }
            else if (AdminTaskService.RunNow(out string why))
            {
                Shutdown();
                return;
            }
            else
            {
                Log.Write("Always start as administrator: the scheduled task couldn't be started (" + why + "), so the app starts normally.");
            }
        }

        _mutex = new Mutex(true, "AUTZ.IronWorks.Preview.SingleInstance", out bool created);
        if (!created)
        {
            _mutex.Dispose();
            _mutex = null;
            Dialogs.Info(null, AppInfo.Name + " is already running (look for its icon in the system tray).");
            Shutdown();
            return;
        }
        _ownsMutex = true;

        var (settings, state) = ProfileStore.LoadActive();

        // the logs hold no names any more: the key that makes the name codes is created once, and logs written by
        // older versions (which did hold names) are deleted once
        bool stateChanged = false;
        if (string.IsNullOrEmpty(state.LogSalt)) { state.LogSalt = Guid.NewGuid().ToString("N"); stateChanged = true; }
        Log.Salt = state.LogSalt;
        if (!state.LogsScrubbed) { Log.DeleteOldLogs(); state.LogsScrubbed = true; stateChanged = true; }
        // "Restart as administrator > Always": this copy is the elevated one, so it remembers the choice (the task is made in PreviewManager.Start)
        if (HasArg(e.Args, "--always-admin") && MemoryCleaner.IsAdministrator && !state.AlwaysAdmin) { state.AlwaysAdmin = true; stateChanged = true; }
        if (stateChanged) ProfileStore.SaveState(state);

        Log.PerSessionFiles = state.DebugLogMode == "PerSession";
        Log.Verbose = state.DebugLogging;
        Log.Trace("APP", $"startup: args={string.Join(" ", e.Args.Select(Log.ArgCode))}; admin={MemoryCleaner.IsAdministrator}; switcher={state.SwitcherEnabled}; memoryCleaner={state.MemoryCleanerEnabled}");
        _manager = new PreviewManager(settings, state);

        // No main window and no taskbar button: the app lives in the system tray (right-click the icon).
        _tray = new NotifyIconService(_manager);
        _manager.Start();

        ShowWhatsNew(state);
    }

    private static bool HasArg(string[] args, string name) =>
        args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static void WaitForPreviousInstance(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (!args[i].Equals("--after-pid", StringComparison.OrdinalIgnoreCase) || !int.TryParse(args[i + 1], out int pid)) continue;
            try
            {
                using var previous = System.Diagnostics.Process.GetProcessById(pid);
                previous.WaitForExit(15000);
            }
            catch
            {
                // it has already gone
            }
        }
    }

    /// <summary>The start-up pop-up with the version and the latest changes (see Settings > About for the options).</summary>
    private void ShowWhatsNew(AppState state)
    {
        string version = AppInfo.Version;
        bool show = state.WhatsNewMode switch
        {
            "Always" => true,
            "Never" => false,
            _ => !state.LastSeenVersion.Equals(version, StringComparison.OrdinalIgnoreCase)
        };

        if (!state.LastSeenVersion.Equals(version, StringComparison.OrdinalIgnoreCase))
        {
            state.LastSeenVersion = version;
            ProfileStore.SaveState(state);
        }
        if (!show) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try { new ChangelogWindow(version).Show(); }
            catch (Exception ex) { Log.Write("What's new window: " + ex.Message); }
        }), DispatcherPriority.ApplicationIdle);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Trace("APP", $"shutdown: exitCode={e.ApplicationExitCode}");
        _tray?.Dispose();
        _manager?.Dispose();
        if (_ownsMutex && _mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
