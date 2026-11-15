using System.Windows;
using AUTZ.IronWorks.Preview.Services;

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

        _mutex = new Mutex(true, "AUTZ.IronWorks.Preview.SingleInstance", out bool created);
        if (!created)
        {
            _mutex.Dispose();
            _mutex = null;
            MessageBox.Show("AUTZ IronWorks Preview is already running (look for it in the system tray).",
                "AUTZ IronWorks Preview", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        _ownsMutex = true;

        var settings = SettingsService.Load();
        _manager = new PreviewManager(settings);
        _tray = new NotifyIconService(_manager, settings);
        _manager.Start();
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
