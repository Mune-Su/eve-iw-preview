using System.Windows;
using AUTZ.IronWorks.Preview.Services;
using AUTZ.IronWorks.Preview.Views;

namespace AUTZ.IronWorks.Preview;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private bool _ownsMutex;
    private PreviewManager? _manager;
    private NotifyIconService? _tray;
    private ControlWindow? _control;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "AUTZ.IronWorks.Preview.SingleInstance", out bool created);
        if (!created)
        {
            _mutex.Dispose();
            _mutex = null;
            MessageBox.Show("AUTZ IronWorks Preview is already running (look for it in the taskbar or system tray).",
                "AUTZ IronWorks Preview", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        _ownsMutex = true;

        var settings = SettingsService.Load();
        _manager = new PreviewManager(settings);

        // The control window is what puts the app in the taskbar. It starts minimized so it stays out of the way.
        _control = new ControlWindow(_manager) { WindowState = WindowState.Minimized, ShowActivated = false };
        _control.Closed += (_, _) => Shutdown();

        _tray = new NotifyIconService(_manager, settings, OpenControlWindow);
        _manager.Start();
        _control.Show();
    }

    private void OpenControlWindow()
    {
        if (_control is null) return;
        _control.Show();
        if (_control.WindowState == WindowState.Minimized) _control.WindowState = WindowState.Normal;
        _control.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
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
