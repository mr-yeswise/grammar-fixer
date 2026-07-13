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
    private CorrectionResult? _lastResult;
    private string _lastInput   = string.Empty;
    private bool   _isCorrecting;

    public CorrectionWindow(LanguageToolClient ltClient, AppController controller)
    {
        InitializeComponent();
        _ltClient   = ltClient;
        _controller = controller;
        _debounce   = new System.Timers.Timer(400) { AutoReset = false };
        _debounce.Elapsed += async (_, _) => await OnTextChangedDebounced();
    }

    // ── XAML-wired event handlers (declared in CorrectionWindow.xaml) ────────

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        InputBox?.Focus();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SavePosition();
    }

    // ── Keyboard ─────────────────────────────────────────────────────────────

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private async void InputBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); return; }
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            await SendToFieldAsync();
            e.Handled = true;
        }
    }

    // ── Correction logic ─────────────────────────────────────────────────────

    private async Task OnTextChangedDebounced()
    {
        var text = await Dispatcher.InvokeAsync(() => InputBox?.Text ?? string.Empty);
        if (string.IsNullOrWhiteSpace(text) || text == _lastInput || _isCorrecting) return;

        _isCorrecting = true;
        await Dispatcher.InvokeAsync(() =>
        {
            if (ProcessingPanel != null) ProcessingPanel.Visibility = Visibility.Visible;
        });

        try
        {
            var result = await _ltClient.CheckAsync(text);
            if (result != null)
            {
                _lastInput = result.Corrected;
                await Dispatcher.InvokeAsync(() =>
                {
                    UpdateDiffView(result);
                    UpdateCounts(result);
                });
            }
        }
        catch (Exception ex) { DiagnosticLogger.Log(DiagnosticLogLevel.Error, $"CorrectionWindow: {ex.Message}"); }
        finally
        {
            _isCorrecting = false;
            await Dispatcher.InvokeAsync(() =>
            {
                if (ProcessingPanel != null) ProcessingPanel.Visibility = Visibility.Collapsed;
            });
        }
    }

    private void UpdateDiffView(CorrectionResult result)
    {
        _lastResult = result;
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

    // ── Button handlers ───────────────────────────────────────────────────────

    private void CopyCorrected_Click(object sender, RoutedEventArgs e)
    {
        var text = GetCorrectedText();
        if (!string.IsNullOrEmpty(text)) WpfClipboard.SetText(text);
    }

    private void ApplyClipboard_Click(object sender, RoutedEventArgs e)
    {
        var text = GetCorrectedText();
        if (!string.IsNullOrEmpty(text)) WpfClipboard.SetText(text);
    }

    private async void SendToField_Click(object sender, RoutedEventArgs e) => await SendToFieldAsync();

    private async Task SendToFieldAsync()
    {
        var text = GetCorrectedText();
        if (!string.IsNullOrWhiteSpace(text))
        {
            _controller.ApplyCorrectionFromWindow(text);
            Close();
        }
        await Task.CompletedTask;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string GetCorrectedText() => _lastResult?.Corrected ?? string.Empty;

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
