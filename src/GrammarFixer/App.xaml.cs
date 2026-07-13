using System.Windows;
using GrammarFixer.Core;
using GrammarFixer.Services;
using GrammarFixer.UI;
using Hardcodet.Wpf.TaskbarNotification;

namespace GrammarFixer;

public partial class App : WpfApp
{
    private AppController?    _controller;
    private TrayIconManager?  _trayIcon;
    private LanguageToolService? _ltService;
    private LanguageToolClient?  _ltClient;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = SettingsService.Load();

        _ltService = new LanguageToolService();
        _ltClient  = new LanguageToolClient();

        var ltReady = await _ltService.StartAsync();

        _controller = new AppController(settings, _ltClient, _ltService);
        _trayIcon   = new TrayIconManager(_controller);
        _trayIcon.Initialize();
        _controller.AttachTrayIcon(_trayIcon);
        _controller.Start();

        if (!ltReady)
        {
            Dispatcher.BeginInvoke(() =>
            {
                _trayIcon.ShowBalloonTip(
                    "LanguageTool Not Ready",
                    "Install Java 11+ and place languagetool-server.jar in tools/. See tools/INSTALL.md",
                    BalloonIcon.Warning);
            });
        }

        AutostartHelper.EnsureAutostart();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Stop();
        _trayIcon?.Dispose();
        _ltService?.Dispose();
        base.OnExit(e);
    }
}
