using System.Windows;
using AUTZ.IronWorks.Preview.Services;
using AUTZ.IronWorks.Preview.Views;

namespace AUTZ.IronWorks.Preview;
public partial class App : System.Windows.Application
{
    private MainWindow? _main;
    private NotifyIconService? _tray;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = SettingsService.Load();
        _main = new MainWindow(settings);
        _tray = new NotifyIconService(_main, settings);
        _main.Show();
    }
    protected override void OnExit(ExitEventArgs e) { _tray?.Dispose(); base.OnExit(e); }
}
