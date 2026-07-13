using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GrammarFixer.Core;
using GrammarFixer.Models;
using GrammarFixer.Services;

namespace GrammarFixer.UI;

public partial class SettingsWindow : Window
{
    private readonly AppController _controller;

    public SettingsWindow(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        LoadValues();
    }

    private void LoadValues()
    {
        var s = _controller.Settings;
        EnabledCheck.IsChecked             = s.Enabled;
        HotkeyBox.Text                     = s.HotkeyTrigger;
        CorrectionWindowHotkeyBox.Text     = s.CorrectionWindowHotkey;
        DebounceBox.Text                   = s.DebounceMs.ToString();
        OneClickRadio.IsChecked            = s.UxMode == UxMode.OneClickRewrite;
        ReviewRadio.IsChecked              = s.UxMode == UxMode.ReviewSuggestions;
        AllowedAppsBox.Text                = string.Join(Environment.NewLine, s.AllowedApps);
        DeniedAppsBox.Text                 = string.Join(Environment.NewLine, s.DeniedApps);
        AutostartCheck.IsChecked           = AutostartHelper.IsAutostartEnabled();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = _controller.Settings;
        s.Enabled                = EnabledCheck.IsChecked == true;
        s.HotkeyTrigger          = HotkeyBox.Text.Trim();
        s.CorrectionWindowHotkey = CorrectionWindowHotkeyBox.Text.Trim();
        s.DebounceMs             = int.TryParse(DebounceBox.Text, out var ms) ? ms : 400;
        s.UxMode                 = ReviewRadio.IsChecked == true ? UxMode.ReviewSuggestions : UxMode.OneClickRewrite;
        s.AllowedApps            = AllowedAppsBox.Text
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        s.DeniedApps             = DeniedAppsBox.Text
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (AutostartCheck.IsChecked == true) AutostartHelper.EnsureAutostart();
        else                                  AutostartHelper.RemoveAutostart();
        _controller.UpdateSettings(s);
        Close();
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticLogger.LogDirectoryPath);
            Process.Start(new ProcessStartInfo { FileName = DiagnosticLogger.LogDirectoryPath, UseShellExecute = true });
        }
        catch { }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
