using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using GrammarFixer.Core;
using GrammarFixer.Models;
using GrammarFixer.Services;

namespace GrammarFixer.UI;

/// <summary>
/// Floating correction window — paste text, auto-corrects via LanguageTool,
/// shows inline diff, one-click copy/apply.
/// </summary>
public partial class CorrectionWindow : Window
{
    private readonly LanguageToolClient  _ltClient;
    private readonly AppController       _controller;
    private readonly System.Timers.Timer _debounce;
    private string _lastInput   = string.Empty;
    private bool   _isCorrecting;

    public CorrectionWindow(LanguageToolClient ltClient, AppController controller)
    {
        InitializeComponent();
        _ltClient   = ltClient;
        _controller = controller;
        _debounce   = new System.Timers.Timer(400) { AutoReset = false };
        _debounce.Elapsed += async (_, _) => await OnTextChangedDebounced();
        Loaded  += (_, _) => { InputBox?.Focus(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Closing += (_, _) => SavePosition();
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private async void InputBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            await SendToFieldAsync();
            e.Handled = true;
        }
    }

    private async Task OnTextChangedDebounced()
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            var text = InputBox?.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text) || text == _lastInput || _isCorrecting) return;
            _isCorrecting = true;
            if (ProcessingPanel != null) ProcessingPanel.Visibility = Visibility.Visible;
            try
            {
                var result = await _ltClient.CheckAsync(text);
                if (result != null)
                {
                    _lastInput = result.Corrected;
                    UpdateDiffView(result);
                    UpdateCounts(result);
                }
            }
            catch (Exception ex) { DiagnosticLogger.Log(DiagnosticLogLevel.Error, $"CorrectionWindow: {ex.Message}"); }
            finally
            {
                _isCorrecting = false;
                if (ProcessingPanel != null) ProcessingPanel.Visibility = Visibility.Collapsed;
            }
        });
    }

    private void UpdateDiffView(CorrectionResult result)
    {
        var diff  = new InlineDiffBuilder(new Differ()).BuildDiffModel(result.Original, result.Corrected);
        var lines = new List<DiffLineViewModel>();
        foreach (var piece in diff.Lines)
        {
            lines.Add(new DiffLineViewModel
            {
                Text = piece.Text,
                Type = piece.Type switch
                {
                    ChangeType.Inserted => DiffType.Insert,
                    ChangeType.Deleted  => DiffType.Delete,
                    ChangeType.Modified => DiffType.Modify,
                    _                   => DiffType.None
                }
            });
        }
        if (DiffItems != null) DiffItems.ItemsSource = lines;
    }

    private void UpdateCounts(CorrectionResult result)
    {
        if (InputCharCount  != null) InputCharCount.Text  = $"{result.Original.Length} chars";
        if (OutputCharCount != null) OutputCharCount.Text = $"{result.Corrected.Length} chars";
        if (EditsCount      != null) EditsCount.Text      = $"{result.Edits.Count} edit{(result.Edits.Count == 1 ? "" : "s")}";
    }

    private void CopyCorrected_Click(object sender, RoutedEventArgs e)
    {
        var text = string.Join(string.Empty,
            DiffItems?.Items.Cast<DiffLineViewModel>().Select(l => l.Text) ?? []);
        WpfClipboard.SetText(text);
        DiagnosticLogger.Log(DiagnosticLogLevel.Info, "Copied corrected text to clipboard");
    }

    private void ApplyClipboard_Click(object sender, RoutedEventArgs e)
    {
        var text = string.Join(string.Empty,
            DiffItems?.Items.Cast<DiffLineViewModel>().Select(l => l.Text) ?? []);
        WpfClipboard.SetText(text);
    }

    private async void SendToField_Click(object sender, RoutedEventArgs e) => await SendToFieldAsync();

    private async Task SendToFieldAsync()
    {
        var text = string.Join(string.Empty,
            DiffItems?.Items.Cast<DiffLineViewModel>().Select(l => l.Text) ?? []);
        if (!string.IsNullOrWhiteSpace(text))
        {
            _controller.ApplyCorrectionFromWindow(text);
            Close();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void SavePosition()
    {
        if (Left >= 0 && Top >= 0)
        {
            _controller.Settings.CorrectionWindowLeft = Left;
            _controller.Settings.CorrectionWindowTop  = Top;
            SettingsService.Save(_controller.Settings);
        }
    }
}
