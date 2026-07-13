using System.Windows;
using GrammarFixer.Core;
using GrammarFixer.Services;
using GrammarFixer.UI;

namespace GrammarFixer;

public partial class App : System.Windows.Application
{
    private AppController? _controller;
    private TrayIconManager? _trayIcon;
    private LanguageToolService? _ltService;
    private LanguageToolClient? _ltClient;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settings = SettingsService.Load();
        
        // Initialize LanguageTool service
        _ltService = new LanguageToolService();
        _ltClient = new LanguageToolClient();
        
        var ltReady = await _ltService.StartAsync();
        
        if (!ltReady)
        {
            // Show notification but continue - app works without corrections
            Dispatcher.BeginInvoke(() =>
            {
                _trayIcon?.ShowBalloonTip(
                    "LanguageTool Not Ready",
                    "Install Java 11+ and LanguageTool server. See tools/INSTALL.md",
                    Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
            });
        }

        _controller = new AppController(settings, _ltClient, _ltService);
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
        _ltService?.Dispose();
        base.OnExit(e);
    }
}