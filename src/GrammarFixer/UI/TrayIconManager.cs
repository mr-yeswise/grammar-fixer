using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using GrammarFixer.Core;
using GrammarFixer.Services;

namespace GrammarFixer.UI;

public sealed partial class TrayIconManager : IDisposable
{
    private TaskbarIcon? _trayIcon;
    private readonly AppController _controller;
    private bool _enabled;
    private bool _isProcessing;

    public TrayIconManager(AppController controller) => _controller = controller;

    public void Initialize()
    {
        _enabled  = _controller.Settings.Enabled;
        _trayIcon = new TaskbarIcon
        {
            IconSource  = LoadIcon("tray_enabled.ico"),
            ToolTipText = "GrammarFixer — LanguageTool Edition",
            ContextMenu = BuildMenu()
        };
        RefreshIcon();
    }

    public void ShowBalloonTip(string title, string message, BalloonIcon icon)
        => _trayIcon?.ShowBalloonTip(title, message, icon);

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        var toggleItem = new MenuItem { Header = _controller.Settings.Enabled ? "Disable" : "Enable", FontWeight = FontWeights.SemiBold };
        toggleItem.Click += (_, _) =>
        {
            var s = _controller.Settings;
            s.Enabled = !s.Enabled;
            _controller.UpdateSettings(s);
            toggleItem.Header = s.Enabled ? "Disable" : "Enable";
            UpdateIcon(s.Enabled);
        };

        var modeItem      = new MenuItem { Header = "Engine: LanguageTool (local)", IsEnabled = false };
        var settingsItem  = new MenuItem { Header = "Settings..." };
        settingsItem.Click += (_, _) => _controller.OpenSettings();

        var cwItem = new MenuItem { Header = "Correction Window  (Ctrl+Alt+Shift+G)" };
        cwItem.Click += (_, _) => _controller.ToggleCorrectionWindow();

        var logsItem = new MenuItem { Header = "📋 View Logs..." };
        logsItem.Click += (_, _) =>
        {
            try { Directory.CreateDirectory(DiagnosticLogger.LogDirectoryPath);
                  Process.Start(new ProcessStartInfo { FileName = DiagnosticLogger.LogDirectoryPath, UseShellExecute = true }); }
            catch { }
        };

        var testItem = new MenuItem { Header = "🧪 Self-Test" };
        testItem.Click += (_, _) => _controller.RunSelfTest();

        var quitItem = new MenuItem { Header = "Exit" };
        quitItem.Click += (_, _) => WpfApp.Current.Shutdown();

        menu.Items.Add(toggleItem);
        menu.Items.Add(modeItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(cwItem);
        menu.Items.Add(logsItem);
        menu.Items.Add(testItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(quitItem);
        return menu;
    }

    private void UpdateIcon(bool enabled) { _enabled = enabled; RefreshIcon(); }

    public void SetProcessingState(bool processing) { _isProcessing = processing; RefreshIcon(); }

    private void RefreshIcon()
    {
        if (_trayIcon == null) return;
        var name = !_enabled ? "tray_disabled.ico" : (_isProcessing ? "tray_processing.ico" : "tray_enabled.ico");
        _trayIcon.IconSource  = LoadIcon(name);
        _trayIcon.ToolTipText = !_enabled ? "GrammarFixer (Disabled)" : (_isProcessing ? "GrammarFixer (Processing...)" : "GrammarFixer");
    }

    private static BitmapImage LoadIcon(string name)
    {
        try   { return new BitmapImage(new Uri($"pack://application:,,,/Assets/{name}", UriKind.Absolute)); }
        catch { return new BitmapImage(new Uri("pack://application:,,,/Assets/tray_enabled.ico", UriKind.Absolute)); }
    }

    public void Dispose() => _trayIcon?.Dispose();
}
