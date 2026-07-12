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
        EnabledCheck.IsChecked      = s.Enabled;
        StaticModeRadio.IsChecked   = s.Mode == CorrectionMode.Static;
        AiModeRadio.IsChecked       = s.Mode == CorrectionMode.AI;
        OneClickRadio.IsChecked     = s.UxMode == UxMode.OneClickRewrite;
        ReviewRadio.IsChecked       = s.UxMode == UxMode.ReviewSuggestions;
        HotkeyBox.Text              = s.HotkeyTrigger;
        DebounceBox.Text            = s.DebounceMs.ToString();
        AllowedAppsBox.Text         = string.Join(Environment.NewLine, s.AllowedApps);
        AutostartCheck.IsChecked    = AutostartHelper.IsAutostartEnabled();

        foreach (ComboBoxItem item in ModelCombo.Items)
            if (item.Tag?.ToString() == s.GroqModel)
            { ModelCombo.SelectedItem = item; break; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = _controller.Settings;
        s.Enabled        = EnabledCheck.IsChecked == true;
        s.Mode           = AiModeRadio.IsChecked == true ? CorrectionMode.AI : CorrectionMode.Static;
        s.UxMode         = ReviewRadio.IsChecked == true ? UxMode.ReviewSuggestions : UxMode.OneClickRewrite;
        s.HotkeyTrigger  = HotkeyBox.Text.Trim();
        s.DebounceMs     = int.TryParse(DebounceBox.Text, out var ms) ? ms : 400;
        s.AllowedApps    = AllowedAppsBox.Text
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        s.GroqModel      = (ModelCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                           ?? "llama-3.1-8b-instant";

        if (AutostartCheck.IsChecked == true)
            AutostartHelper.EnsureAutostart();
        else
            AutostartHelper.RemoveAutostart();

        _controller.UpdateSettings(s);
        Close();
    }

    private void SaveApiKey_Click(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Password.Trim();
        if (!string.IsNullOrEmpty(key))
        {
            CredentialService.SaveApiKey(key);
            // Use fully-qualified WPF MessageBox to avoid WinForms ambiguity
            WpfMessageBox.Show("API key saved securely in Windows Credential Manager.",
                "GrammarFixer", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
