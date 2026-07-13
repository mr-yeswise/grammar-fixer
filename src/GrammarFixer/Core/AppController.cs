using System.Windows.Input;
using System.Timers;
using GrammarFixer.Models;
using GrammarFixer.Services;
using GrammarFixer.UI;

namespace GrammarFixer.Core;

/// <summary>
/// Central orchestrator: hook → debounce → UIA read → LT correct → pill → apply.
/// </summary>
public sealed class AppController : IDisposable
{
    private AppSettings _settings;
    private readonly CorrectionPipeline  _pipeline;
    private readonly LanguageToolClient  _ltClient;
    private readonly LanguageToolService _ltService;
    private readonly HotkeyManager _hotkey;
    private readonly HotkeyManager _correctionWindowHotkey;
    private readonly KeyboardHook   _typingHook;
    private OverlayWindow?     _overlay;
    private FloatingButton?    _floatingBtn;
    private CorrectionWindow?  _correctionWindow;
    private bool _disposed;
    private Action<bool>? _setProcessingState;

    private string?          _lastCapturedText;
    private CorrectionResult? _lastResult;

    private readonly System.Timers.Timer _typingDebounce;

    public AppSettings Settings => _settings;

    public AppController(AppSettings settings, LanguageToolClient ltClient, LanguageToolService ltService)
    {
        _settings  = settings;
        _ltClient  = ltClient;
        _ltService = ltService;
        _pipeline  = new CorrectionPipeline(settings, ltClient);
        _pipeline.CorrectionReady += OnCorrectionReady;

        _hotkey = new HotkeyManager(settings.HotkeyTrigger);
        _hotkey.HotkeyPressed += OnHotkeyPressed;

        _correctionWindowHotkey = new HotkeyManager(settings.CorrectionWindowHotkey);
        _correctionWindowHotkey.HotkeyPressed += OnCorrectionWindowHotkeyPressed;

        _typingHook = new KeyboardHook();
        _typingHook.KeyDown += OnTypingKeyDown;

        _typingDebounce = new System.Timers.Timer(settings.DebounceMs) { AutoReset = false };
        _typingDebounce.Elapsed += async (_, _) => await OnTypingPaused();
    }

    public void Start()
    {
        _hotkey.Start();
        _correctionWindowHotkey.Start();
        _typingHook.Install();
        DiagnosticLogger.Log(DiagnosticLogLevel.Info, "AppController started");
        WpfApp.Current.Dispatcher.Invoke(() => { _floatingBtn = new FloatingButton(this); });
    }

    public void Stop()
    {
        _hotkey.Stop();
        _correctionWindowHotkey.Stop();
        _typingHook.Uninstall();
        _typingDebounce.Stop();
        WpfApp.Current.Dispatcher.Invoke(() =>
        {
            _overlay?.Close();
            _correctionWindow?.Close();
            _floatingBtn?.Close();
        });
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        _pipeline.UpdateSettings(settings);
        _typingDebounce.Interval = settings.DebounceMs;
        _hotkey.UpdateHotkey(settings.HotkeyTrigger);
        _correctionWindowHotkey.UpdateHotkey(settings.CorrectionWindowHotkey);
        SettingsService.Save(settings);
    }

    public void OpenSettings()
    {
        WpfApp.Current.Dispatcher.Invoke(() => { new SettingsWindow(this).Show(); });
    }

    public void ToggleCorrectionWindow()
    {
        WpfApp.Current.Dispatcher.Invoke(() =>
        {
            if (_correctionWindow == null || !_correctionWindow.IsLoaded)
            {
                _correctionWindow = new CorrectionWindow(_ltClient, this);
                _correctionWindow.Closed += (_, _) => _correctionWindow = null;
                if (_settings.CorrectionWindowLeft >= 0 && _settings.CorrectionWindowTop >= 0)
                {
                    _correctionWindow.Left = _settings.CorrectionWindowLeft;
                    _correctionWindow.Top  = _settings.CorrectionWindowTop;
                }
                _correctionWindow.Show();
            }
            else
            {
                _correctionWindow.Close();
            }
        });
    }

    private void OnCorrectionWindowHotkeyPressed()
    {
        if (!_settings.Enabled) return;
        ToggleCorrectionWindow();
    }

    /// <summary>Pill clicked — apply all corrections in one shot (Quillbot style).</summary>
    public void TriggerFromFloatingButton()
    {
        if (_lastResult != null)
        {
            WpfApp.Current.Dispatcher.Invoke(() =>
            {
                if (_settings.UxMode == UxMode.OneClickRewrite)
                    UiaHelper.SetFocusedText(_lastResult.Corrected);
                else
                    ShowOverlay(_lastResult);
                _floatingBtn?.Hide();
            });
        }
        else if (_lastCapturedText != null)
        {
            _ = Task.Run(async () =>
            {
                var r = await _pipeline.CorrectNowAsync(_lastCapturedText);
                if (r == null || r.Corrected == r.Original) return;
                _lastResult = r;
                WpfApp.Current.Dispatcher.Invoke(() =>
                {
                    if (_settings.UxMode == UxMode.OneClickRewrite)
                        UiaHelper.SetFocusedText(r.Corrected);
                    else
                        ShowOverlay(r);
                    _floatingBtn?.Hide();
                });
            });
        }
    }

    private async void OnHotkeyPressed()
    {
        if (!_settings.Enabled) return;
        var processName = UiaHelper.GetForegroundProcessName();
        if (!IsAppAllowed(processName)) return;
        var text = UiaHelper.GetFocusedText();
        if (string.IsNullOrWhiteSpace(text)) return;
        var result = await _pipeline.CorrectNowAsync(text);
        if (result == null || result.Corrected == result.Original) return;
        WpfApp.Current.Dispatcher.Invoke(() => UiaHelper.SetFocusedText(result.Corrected));
    }

    private void OnTypingKeyDown(Key key)
    {
        if (!_settings.Enabled || _settings.HotkeyOnlyMode) return;
        if (_settings.DebugMode) DiagnosticLogger.Log(DiagnosticLogLevel.Debug, $"Key: {key}");
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        _typingDebounce.Stop();
        _typingDebounce.Start();
    }

    private async Task OnTypingPaused()
    {
        if (!_settings.Enabled) return;
        _setProcessingState?.Invoke(true);
        string? processName = null;
        string? text        = null;
        WpfPoint caretPos   = default;
        try
        {
            await WpfApp.Current.Dispatcher.InvokeAsync(() =>
            {
                processName = UiaHelper.GetForegroundProcessName();
            });
            DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"Typing paused. Process: {processName}");
            if (!IsAppAllowed(processName)) return;

            await WpfApp.Current.Dispatcher.InvokeAsync(() =>
            {
                text      = UiaHelper.GetFocusedText();
                caretPos  = UiaHelper.GetCaretScreenPosition();
            });
            if (string.IsNullOrWhiteSpace(text)) return;

            DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"Captured {text!.Length} chars");
            _lastCapturedText = text;
            _lastResult       = null;

            var result = await _pipeline.CorrectNowAsync(text);
            if (result != null && result.Corrected != result.Original)
            {
                _lastResult = result;
                DiagnosticLogger.Log(DiagnosticLogLevel.Info, "Correction ready — showing pill");
                WpfApp.Current.Dispatcher.Invoke(() => _floatingBtn?.ShowAt(caretPos));
            }
            else DiagnosticLogger.Log(DiagnosticLogLevel.Info, "No correction needed");
        }
        finally { _setProcessingState?.Invoke(false); }
    }

    public void AttachTrayIcon(TrayIconManager t) => _setProcessingState = t.SetProcessingState;

    /// <summary>Apply correction from OverlayWindow accept button.</summary>
    public void ApplyCorrection(CorrectionResult result)
    {
        UiaHelper.SetFocusedText(result.Corrected);
        WpfApp.Current.Dispatcher.Invoke(() => { _overlay?.Close(); _overlay = null; });
    }

    /// <summary>Dismiss OverlayWindow without applying.</summary>
    public void DismissOverlay()
    {
        WpfApp.Current.Dispatcher.Invoke(() => { _overlay?.Close(); _overlay = null; });
    }

    private void ShowOverlay(CorrectionResult result)
    {
        _overlay?.Close();
        var pos = UiaHelper.GetCaretScreenPosition();
        _overlay = new OverlayWindow(result, pos, this);
        _overlay.Show();
    }

    public void ApplyCorrectionFromWindow(string correctedText)
        => UiaHelper.SetFocusedText(correctedText);

    public void RunSelfTest()
    {
        const string sample = "i recieve the freind definately";
        var failures = new List<string>();
        try
        {
            var result = _pipeline.CorrectNowAsync(sample).GetAwaiter().GetResult();
            if (result == null) { failures.Add("Pipeline returned null"); }
            else
            {
                foreach (var (word, ok) in new[]
                {
                    ("receive",    result.Corrected.Contains("receive",    StringComparison.OrdinalIgnoreCase)),
                    ("friend",     result.Corrected.Contains("friend",     StringComparison.OrdinalIgnoreCase)),
                    ("definitely", result.Corrected.Contains("definitely", StringComparison.OrdinalIgnoreCase))
                })
                {
                    if (ok) DiagnosticLogger.Log(DiagnosticLogLevel.Info,  $"Self-test PASS: '{word}'");
                    else  { DiagnosticLogger.Log(DiagnosticLogLevel.Error, $"Self-test FAIL: '{word}'"); failures.Add(word); }
                }
            }
        }
        catch (Exception ex) { failures.Add(ex.Message); }

        WpfApp.Current.Dispatcher.Invoke(() =>
        {
            if (failures.Count == 0)
                WpfMessageBox.Show("Self-test: 3/3 passed ✓", "GrammarFixer",
                    WpfMsgBoxButton.OK, WpfMsgBoxImage.Information);
            else
                WpfMessageBox.Show(
                    $"Failures:{Environment.NewLine}{string.Join("\n", failures.Select(x => $"  - {x}"))}",
                    "GrammarFixer", WpfMsgBoxButton.OK, WpfMsgBoxImage.Warning);
        });
    }

    private void OnCorrectionReady(CorrectionResult result)
    {
        if (!_settings.Enabled || result.Corrected == result.Original) return;
        _lastResult = result;
        WpfApp.Current.Dispatcher.Invoke(() =>
        {
            var pos = UiaHelper.GetCaretScreenPosition();
            _floatingBtn?.ShowAt(pos);
        });
    }

    private bool IsAppAllowed(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;
        var denied = _settings.DeniedApps.FirstOrDefault(d =>
            processName.Contains(d, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(denied)) { DiagnosticLogger.Log(DiagnosticLogLevel.Warn, $"Blocked: {denied}"); return false; }
        if (_settings.AllowedApps.Count == 0) return true;
        return _settings.AllowedApps.Any(a => processName.Contains(a, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hotkey.Dispose();
        _correctionWindowHotkey.Dispose();
        _typingHook.Dispose();
        _typingDebounce.Dispose();
        _pipeline.Dispose();
    }
}
