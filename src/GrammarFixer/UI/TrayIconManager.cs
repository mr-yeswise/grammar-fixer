using System.Windows;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using GrammarFixer.Core;
using GrammarFixer.Models;

namespace GrammarFixer.UI;

/// <summary>
/// System tray icon — enable/disable toggle, mode switch, settings, quit.
/// Uses Hardcodet.NotifyIcon.Wpf.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private TaskbarIcon? _trayIcon;
    private readonly AppController _controller;

    public TrayIconManager(AppController controller)
    {
        _controller = controller;
    }

    public void Initialize()
    {
        _trayIcon = new TaskbarIcon
        {
            IconSource = LoadIcon("tray_enabled.ico"),
            ToolTipText = "GrammarFixer",
            ContextMenu = BuildMenu()
        };
    }

    private System.Windows.Controls.ContextMenu BuildMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var toggleItem = new System.Windows.Controls.MenuItem
        {
            Header = _controller.Settings.Enabled ? "Disable" : "Enable",
            FontWeight = FontWeights.SemiBold
        };
        toggleItem.Click += (_, _) =>
        {
            var s = _controller.Settings;
            s.Enabled = !s.Enabled;
            _controller.UpdateSettings(s);
            toggleItem.Header = s.Enabled ? "Disable" : "Enable";
            UpdateIcon(s.Enabled);
        };

        var modeItem = new System.Windows.Controls.MenuItem
        {
            Header = $"Mode: {_controller.Settings.Mode}"
        };
        modeItem.Click += (_, _) =>
        {
            var s = _controller.Settings;
            s.Mode = s.Mode == CorrectionMode.Static ? CorrectionMode.AI : CorrectionMode.Static;
            _controller.UpdateSettings(s);
            modeItem.Header = $"Mode: {s.Mode}";
        };

        var settingsItem = new System.Windows.Controls.MenuItem { Header = "Settings..." };
        settingsItem.Click += (_, _) => _controller.OpenSettings();

        var quitItem = new System.Windows.Controls.MenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => WpfApp.Current.Shutdown();

        menu.Items.Add(toggleItem);
        menu.Items.Add(modeItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(quitItem);

        return menu;
    }

    private void UpdateIcon(bool enabled)
    {
        if (_trayIcon == null) return;
        _trayIcon.IconSource  = LoadIcon(enabled ? "tray_enabled.ico" : "tray_disabled.ico");
        _trayIcon.ToolTipText = enabled ? "GrammarFixer (Active)" : "GrammarFixer (Disabled)";
    }

    private static BitmapImage LoadIcon(string name)
    {
        var uri = new Uri($"pack://application:,,,/Assets/{name}", UriKind.Absolute);
        return new BitmapImage(uri);
    }

    public void Dispose() => _trayIcon?.Dispose();
}
