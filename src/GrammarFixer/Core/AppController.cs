using System.Windows.Input;
using System.Windows;
using System.Timers;
using GrammarFixer.Models;
using GrammarFixer.Services;
using GrammarFixer.UI;

namespace GrammarFixer.Core;

/// <summary>
/// Central orchestrator.
/// Flow:
///   KeyboardHook fires on every keypress
///   -> debounce 400ms
///   -> read focused text via UIA
///   -> CorrectionPipeline corrects via LanguageTool
///   -> show FloatingButton pill near caret
///   -> on pill click: apply all corrections in one shot (Quillbot style)
/// </summary>
public sealed class AppController : IDisposable
{
    private AppSettings _settings;
    private readonly CorrectionPipeline _pipeline;
    private readonly LanguageToolClient _ltClient;
    private readonly LanguageToolService _ltService;
    private readonly HotkeyManager _hotkey;
    private readonly HotkeyManager _correctionWindowHotkey;
    private readonly KeyboardHook _typingHook;
    private OverlayWindow? _overlay;
    private FloatingButton? _floatingBtn;
    private bool _disposed;
    private Action<bool>? _setProcessingState;

    private string? _lastCapturedText;
    private CorrectionResult? _lastResult;

    // Standard Timers.Timer — Stop/Start/Interval/Elapsed all work
    private readonly System.Timers.Timer _typingDebounce;

    public AppSettings Settings => _settings;

    public AppController(AppSettings settings, LanguageToolClient ltClient, LanguageToolService ltService)
    {
        _settings = settings;
        _ltClient = ltClient;
        _ltService = ltService;
        _pipeline = new CorrectionPipeline(settings, ltClient);
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
        DiagnosticLogger.Log(DiagnosticLogLevel.Info, "AppController started, hooks installed");

        WpfApp.Current.Dispatcher.Invoke(() =>
        {
            _floatingBtn = new FloatingButton(this);
        });
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
        WpfApp.Current.Dispatcher.Invoke(() =>
        {
            var win = new SettingsWindow(this);
            win.Show();
        });
    }

    private void OnCorrectionWindowHotkeyPressed()
    {
        if (!_settings.Enabled) return;
        OpenSettings(); // placeholder until CorrectionWindow is built
    }

    /// <summary>Called from FloatingButton click — applies all corrections in one shot.</summary>
    public void TriggerFromFloatingButton()
    {
        if (_lastResult != null)
        {
            WpfApp.Current.Dispatcher.Invoke(() =>
            {
                UiaHelper.SetFocusedText(_lastResult.Corrected);
                _floatingBtn?.Hide();
            });
        }
        else if (_lastCapturedText != null)
        {
            _ = Task.Run(async () =>
            {
                var result = await _pipeline.CorrectNowAsync(_lastCapturedText);
                if (result == null || result.Corrected == result.Original) return;
                _lastResult = result;
                WpfApp.Current.Dispatcher.Invoke(() =>
                {
                    UiaHelper.SetFocusedText(result.Corrected);
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

        WpfApp.Current.Dispatcher.Invoke(() =>
        {
            UiaHelper.SetFocusedText(result.Corrected);
        });
    }

    private void OnTypingKeyDown(Key key)
    {
        if (!_settings.Enabled) return;
        if (_settings.HotkeyOnlyMode) return;
        if (_settings.DebugMode)
            DiagnosticLogger.Log(DiagnosticLogLevel.Debug, $"Key pressed: {key}");

        if (key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin) return;

        _typingDebounce.Stop();
        _typingDebounce.Start();
    }

    private async Task OnTypingPaused()
    {
        if (!_settings.Enabled) return;

        _setProcessingState?.Invoke(true);
        string? processName = null;
        string? text = null;
        WpfPoint caretPos = default;

        try
        {
            await WpfApp.Current.Dispatcher.InvokeAsync(() =>
            {
                processName = UiaHelper.GetForegroundProcessName();
            });

            DiagnosticLogger.Log(DiagnosticLogLevel.Info,
                $"Typing paused. Process: {processName ?? "unknown"}");

            if (!IsAppAllowed(processName))
            {
                DiagnosticLogger.Log(DiagnosticLogLevel.Warn,
                    $"Process '{processName}' blocked — skipping");
                return;
            }

            await WpfApp.Current.Dispatcher.InvokeAsync(() =>
            {
                text = UiaHelper.GetFocusedText();
                caretPos = UiaHelper.GetCaretScreenPosition();
            });

            if (string.IsNullOrWhiteSpace(text)) return;

            var preview = text.Substring(0, Math.Min(40, text.Length));
            DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"Captured {text.Length} chars: '{preview}'");

            _lastCapturedText = text;
            _lastResult = null;

            var result = await _pipeline.CorrectNowAsync(text);
            if (result != null && result.Corrected != result.Original)
            {
                _lastResult = result;
                DiagnosticLogger.Log(DiagnosticLogLevel.Info, "Correction ready. Showing pill.");
                WpfApp.Current.Dispatcher.Invoke(() => _floatingBtn?.ShowAt(caretPos));
            }
            else
            {
                DiagnosticLogger.Log(DiagnosticLogLevel.Info, "No correction needed");
            }
        }
        finally
        {
            _setProcessingState?.Invoke(false);
        }
    }

    public void AttachTrayIcon(TrayIconManager trayIconManager)
    {
        _setProcessingState = trayIconManager.SetProcessingState;
    }

    public void RunSelfTest()
    {
        const string sample = "i recieve the freind definately";
        var failures = new List<string>();

        try
        {
            var result = _pipeline.CorrectNowAsync(sample).GetAwaiter().GetResult();
            if (result == null)
            {
                failures.Add("Pipeline returned null");
            }
            else
            {
                var checks = new[]
                {
                    ("receive",    result.Corrected.Contains("receive",    StringComparison.OrdinalIgnoreCase)),
                    ("friend",     result.Corrected.Contains("friend",     StringComparison.OrdinalIgnoreCase)),
                    ("definitely", result.Corrected.Contains("definitely", StringComparison.OrdinalIgnoreCase))
                };
                foreach (var (word, passed) in checks)
                {
                    if (passed) DiagnosticLogger.Log(DiagnosticLogLevel.Info,  $"Self-test PASS: '{word}'");
                    else        { failures.Add(word); DiagnosticLogger.Log(DiagnosticLogLevel.Error, $"Self-test FAIL: '{word}'"); }
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add(ex.Message);
            DiagnosticLogger.Log(DiagnosticLogLevel.Error, $"Self-test exception: {ex.Message}");
        }

        WpfApp.Current.Dispatcher.Invoke(() =>
        {
            if (failures.Count == 0)
                WpfMessageBox.Show("Self-test: 3/3 passed ✓", "GrammarFixer Self-Test",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            else
                WpfMessageBox.Show(
                    $"Self-test failed:{Environment.NewLine}{string.Join(Environment.NewLine, failures.Select(x => $"  - {x}"))}",
                    "GrammarFixer Self-Test", MessageBoxButton.OK, MessageBoxImage.Warning);
        });
    }

    private void OnCorrectionReady(CorrectionResult result)
    {
        if (!_settings.Enabled) return;
        if (result.Corrected == result.Original) return;
        _lastResult = result;
        WpfApp.Current.Dispatcher.Invoke(() =>
        {
            var pos = UiaHelper.GetCaretScreenPosition();
            _floatingBtn?.ShowAt(pos);
        });
    }

    public void ApplyCorrectionFromWindow(string correctedText)
    {
        UiaHelper.SetFocusedText(correctedText);
    }

    private bool IsAppAllowed(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Warn, "Process check: no process name");
            return false;
        }

        var denied = _settings.DeniedApps.FirstOrDefault(d =>
            processName.Contains(d, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(denied))
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"Blocked by DeniedApps: '{denied}'");
            return false;
        }

        if (_settings.AllowedApps.Count == 0) return true;

        var allowed = _settings.AllowedApps.FirstOrDefault(a =>
            processName.Contains(a, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(allowed)) return true;

        DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"Blocked (not in AllowedApps): '{processName}'");
        return false;
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
