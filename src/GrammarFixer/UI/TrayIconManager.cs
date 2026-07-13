using System.Windows;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using GrammarFixer.Core;
using GrammarFixer.Models;
using GrammarFixer.Services;

namespace GrammarFixer.UI;

/// <summary>
/// System tray icon — enable/disable toggle, mode switch, settings, quit.
/// Uses Hardcodet.NotifyIcon.Wpf.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private TaskbarIcon? _trayIcon;
    private readonly AppController _controller;
    private bool _enabled;
    private bool _isProcessing;

    public TrayIconManager(AppController controller)
    {
        _controller = controller;
    }

    public void Initialize()
    {
        _enabled = _controller.Settings.Enabled;
        _trayIcon = new TaskbarIcon
        {
            IconSource = LoadIcon("tray_enabled.ico"),
            ToolTipText = "GrammarFixer",
            ContextMenu = BuildMenu()
        };
        RefreshIcon();
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

        var viewLogsItem = new System.Windows.Controls.MenuItem { Header = "📋 View Logs..." };
        viewLogsItem.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(DiagnosticLogger.LogDirectoryPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = DiagnosticLogger.LogDirectoryPath,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore shell-start errors
            }
        };

        var selfTestItem = new System.Windows.Controls.MenuItem { Header = "🧪 Run Self-Test" };
        selfTestItem.Click += (_, _) => _controller.RunSelfTest();

        var quitItem = new System.Windows.Controls.MenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => WpfApp.Current.Shutdown();

        menu.Items.Add(toggleItem);
        menu.Items.Add(modeItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(viewLogsItem);
        menu.Items.Add(selfTestItem);
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(quitItem);

        return menu;
    }

    private void UpdateIcon(bool enabled)
    {
        _enabled = enabled;
        RefreshIcon();
    }

    public void SetProcessingState(bool isProcessing)
    {
        _isProcessing = isProcessing;
        RefreshIcon();
    }

    private void RefreshIcon()
    {
        if (_trayIcon == null) return;
        var iconName = !_enabled
            ? "tray_disabled.ico"
            : (_isProcessing ? "tray_processing.ico" : "tray_enabled.ico");
        _trayIcon.IconSource = LoadIcon(iconName);
        _trayIcon.ToolTipText = !_enabled
            ? "GrammarFixer (Disabled)"
            : (_isProcessing ? "GrammarFixer (Processing...)" : "GrammarFixer (Active)");
    }

    private static BitmapImage LoadIcon(string name)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/Assets/{name}", UriKind.Absolute);
            return new BitmapImage(uri);
        }
        catch
        {
            var fallback = new Uri("pack://application:,,,/Assets/tray_enabled.ico", UriKind.Absolute);
            return new BitmapImage(fallback);
        }
    }

    public void Dispose() => _trayIcon?.Dispose();
}
