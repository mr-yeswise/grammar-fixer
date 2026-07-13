using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using GrammarFixer.Core;
using GrammarFixer.Models;
using GrammarFixer.Services;

namespace GrammarFixer.UI;

/// <summary>
/// Floating correction window for paste-and-correct workflow.
/// TextArea with auto-correct, inline diff view, and action buttons.
/// </summary>
public partial class CorrectionWindow : Window
{
    private readonly LanguageToolClient _ltClient;
    private readonly AppController _controller;
    private readonly System.Timers.Timer _debounce;
    private string _lastInput = "";
    private bool _isCorrecting;
    private bool _isLoaded;

    public CorrectionWindow(LanguageToolClient ltClient, AppController controller)
    {
        InitializeComponent();
        
        _ltClient = ltClient;
        _controller = controller;
        
        _debounce = new System.Timers.Timer(300) { AutoReset = false };
        _debounce.Elapsed += async (_, _) => await OnTextChangedDebounced();
        
        Closing += (_, _) => SavePosition();
        Loaded += (_, _) => 
        {
            _isLoaded = true;
            InputBox.Focus();
        };
        
        KeyDown += (_, e) => 
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    private async void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private async void InputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            // Ctrl+Enter: send to field
            await SendToFieldAsync();
            e.Handled = true;
        }
    }

    private async Task OnTextChangedDebounced()
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            var text = InputBox.Text;
            if (string.IsNullOrWhiteSpace(text) || text == _lastInput || _isCorrecting)
                return;

            _isCorrecting = true;
            ProcessingPanel.Visibility = Visibility.Visible;

            try
            {
                // Use the LT service base URL
                var baseUrl = _controller.Settings.Mode == CorrectionMode.LanguageTool 
                    ? _ltClient.GetType().GetProperty("BaseUrl")?.GetValue(_ltClient) as string ?? "http://localhost:8081"
                    : "http://localhost:8081";

                var result = await _ltClient.CheckAsync(text, baseUrl);
                
                if (result != null && result.Corrected != result.Original)
                {
                    _lastInput = result.Corrected;
                    UpdateDiffView(result);
                    UpdateCounts(result);
                }
                else if (result != null)
                {
                    // No corrections - still show clean diff
                    UpdateDiffView(result);
                    UpdateCounts(result);
                }
            }
            catch (Exception ex)
            {
                DiagnosticLogger.Log(DiagnosticLogLevel.Error, $"CorrectionWindow error: {ex.Message}");
            }
            finally
            {
                _isCorrecting = false;
                ProcessingPanel.Visibility = Visibility.Collapsed;
            }
        });
    }

    private void UpdateDiffView(CorrectionResult result)
    {
        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(result.Original, result.Corrected);

        var lines = new List<DiffLineViewModel>();
        foreach (var piece in diff.Lines)
        {
            lines.Add(new DiffLineViewModel
            {
                Text = piece.Text + (piece.Text.EndsWith("\n") ? "" : "\n"),
                Type = piece.Type switch
                {
                    ChangeType.Inserted => DiffType.Insert,
                    ChangeType.Deleted => DiffType.Delete,
                    ChangeType.Modified => DiffType.Modify,
                    _ => DiffType.None
                }
            });
        }

        DiffItems.ItemsSource = lines;
    }

    private void UpdateCounts(CorrectionResult result)
    {
        InputCharCount.Text = $"{result.Original.Length} chars";
        OutputCharCount.Text = $"{result.Corrected.Length} chars";
        EditsCount.Text = $"{result.Edits.Count} edit{(result.Edits.Count == 1 ? "" : "s")}";
    }

    private async void CopyCorrected_Click(object sender, RoutedEventArgs e)
    {
        var diffText = string.Join("", DiffItems.Items.Cast<DiffLineViewModel>().Select(l => l.Text));
        Clipboard.SetText(diffText);
        ShowToast("Copied to clipboard");
    }

    private async void ApplyClipboard_Click(object sender, RoutedEventArgs e)
    {
        var diffText = string.Join("", DiffItems.Items.Cast<DiffLineViewModel>().Select(l => l.Text));
        Clipboard.SetText(diffText);
        ShowToast("Applied to clipboard");
    }

    private async void SendToField_Click(object sender, RoutedEventArgs e)
    {
        await SendToFieldAsync();
    }

    private async Task SendToFieldAsync()
    {
        var diffText = string.Join("", DiffItems.Items.Cast<DiffLineViewModel>().Select(l => l.Text));
        if (string.IsNullOrWhiteSpace(diffText))
            return;

        _controller.ApplyCorrectionFromWindow(diffText);
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SavePosition();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        InputBox.Focus();
        InputBox.CaretIndex = InputBox.Text.Length;
    }

    private void SavePosition()
    {
        if (Left >= 0 && Top >= 0)
        {
            _controller.Settings.CorrectionWindowLeft = Left;
            _controller.Settings.CorrectionWindowTop = Top;
            SettingsService.Save(_controller.Settings);
        }
    }

    private void ShowToast(string message)
    {
        // Could add a toast notification here
        DiagnosticLogger.Log(DiagnosticLogLevel.Info, message);
    }
}