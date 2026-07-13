using System.Windows;
using GrammarFixer.Core;
using GrammarFixer.Services;
using GrammarFixer.UI;

namespace GrammarFixer;

public partial class App : WpfApp
{
    private AppController? _controller;
    private TrayIconManager? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = SettingsService.Load();
        _controller = new AppController(settings);
        _trayIcon = new TrayIconManager(_controller);
        _trayIcon.Initialize();
        _controller.AttachTrayIcon(_trayIcon);
        _controller.Start();
        AutostartHelper.EnsureAutostart();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Stop();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
