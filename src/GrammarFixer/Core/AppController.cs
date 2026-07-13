using System.Windows.Input;
using System.Windows;
using System.IO;
using System.Timers;
using GrammarFixer.Models;
using GrammarFixer.Services;
using GrammarFixer.UI;

namespace GrammarFixer.Core;

/// <summary>
/// Central orchestrator.
/// Flow:
///   KeyboardHook fires on every keypress
///   -> debounce 300ms
///   -> read focused text via UIA
///   -> CorrectionPipeline corrects via LanguageTool
///   -> show FloatingButton pill near caret
///   -> on pill click OR Ctrl+Alt+G: apply correction
///   -> Ctrl+Alt+Shift+G opens CorrectionWindow for paste & correct
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
    private CorrectionWindow? _correctionWindow;
    private bool _disposed;
    private Action<bool>? _setProcessingState;

    private string? _lastCapturedText;
    private CorrectionResult? _lastResult;

    private readonly WpfTimer _typingDebounce;

    public AppSettings Settings => _settings;

    public AppController(AppSettings settings, LanguageToolClient ltClient, LanguageToolService ltService)
    {
        _settings = settings;
        _ltClient = ltClient;
        _ltService = ltService;
        _pipeline = new CorrectionPipeline(settings, ltClient, ltService);
        _pipeline.CorrectionReady += OnCorrectionReady;

        _hotkey = new HotkeyManager(settings.HotkeyTrigger);
        _hotkey.HotkeyPressed += OnHotkeyPressed;

        _correctionWindowHotkey = new HotkeyManager(settings.CorrectionWindowHotkey);
        _correctionWindowHotkey.HotkeyPressed += OnCorrectionWindowHotkey;

        _typingHook = new KeyboardHook();
        _typingHook.KeyDown += OnTypingKeyDown;

        _typingDebounce = new WpfTimer(settings.DebounceMs) { AutoReset = false };
        _typingDebounce.Elapsed += async (_, _) => await OnTypingPaused();
    }

    public void Start()
    {
        _hotkey.Start();
        _correctionWindowHotkey.Start();
        _typingHook.Install();
        DiagnosticLogger.Log(DiagnosticLogLevel.Info, "AppController started, hooks installed");

        Application.Current.Dispatcher.Invoke(() =>
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
        
        Application.Current.Dispatcher.Invoke(() =>
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
        Application.Current.Dispatcher.Invoke(() =>
        {
            var win = new SettingsWindow(this);
            win.Show();
        });
    }

    public void ToggleCorrectionWindow()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_correctionWindow == null || !_correctionWindow.IsLoaded)
            {
                _correctionWindow = new CorrectionWindow(_ltClient, this);
                _correctionWindow.Closed += (_, _) => _correctionWindow = null;
                
                // Restore position
                if (_settings.CorrectionWindowLeft >= 0 && _settings.CorrectionWindowTop >= 0)
                {
                    _correctionWindow.Left = _settings.CorrectionWindowLeft;
                    _correctionWindow.Top = _settings.CorrectionWindowTop;
                }
                
                _correctionWindow.Show();
            }
            else
            {
                _correctionWindow.Close();
            }
        });
    }

    private void OnCorrectionWindowHotkey()
    {
        if (!_settings.Enabled) return;
        ToggleCorrectionWindow();
    }

    // Called from FloatingButton click
    public void TriggerFromFloatingButton()
    {
        if (_lastResult != null)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_settings.UxMode == UxMode.OneClickRewrite)
                    UiaHelper.SetFocusedText(_lastResult.Corrected);
                else
                    ShowOverlay(_lastResult);
            });
        }
        else if (_lastCapturedText != null)
        {
            _ = Task.Run(async () =>
            {
                var result = await _pipeline.CorrectNowAsync(_lastCapturedText);
                if (result == null || result.Corrected == result.Original) return;
                
                _lastResult = result;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_settings.UxMode == UxMode.OneClickRewrite)
                        UiaHelper.SetFocusedText(result.Corrected);
                    else
                        ShowOverlay(result);
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

        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_settings.UxMode == UxMode.OneClickRewrite)
                UiaHelper.SetFocusedText(result.Corrected);
            else
                ShowOverlay(result);
        });
    }

    private void OnTypingKeyDown(Key key)
    {
        if (!_settings.Enabled) return;
        if (_settings.HotkeyOnlyMode) return;
        if (_settings.DebugMode)
            DiagnosticLogger.Log(DiagnosticLogLevel.Debug, $"Key pressed: {key}");

        // Ignore modifier keys
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
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                processName = UiaHelper.GetForegroundProcessName();
            });

            DiagnosticLogger.Log(DiagnosticLogLevel.Info,
                $"Typing paused. Reading UIA text from process: {processName ?? "unknown"}");

            if (!IsAppAllowed(processName))
            {
                DiagnosticLogger.Log(DiagnosticLogLevel.Warn,
                    $"Process '{processName ?? "unknown"}' is blocked — skipping");
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                text = UiaHelper.GetFocusedText();
                caretPos = UiaHelper.GetCaretScreenPosition();
            });

            if (string.IsNullOrWhiteSpace(text))
                return;

            var preview = text.Substring(0, Math.Min(40, text.Length));
            DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"Captured {text.Length} chars: '{preview}...'");

            _lastCapturedText = text;
            _lastResult = null;

            var result = await _pipeline.CorrectNowAsync(text);
            if (result != null && result.Corrected != result.Original)
            {
                _lastResult = result;
                var originalPreview = result.Original.Substring(0, Math.Min(40, result.Original.Length));
                var correctedPreview = result.Corrected.Substring(0, Math.Min(40, result.Corrected.Length));
                DiagnosticLogger.Log(DiagnosticLogLevel.Info,
                    $"Correction ready. Original: '{originalPreview}...' → Corrected: '{correctedPreview}...'. Showing pill.");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    _floatingBtn?.ShowAt(caretPos);
                });
            }
            else
            {
                DiagnosticLogger.Log(DiagnosticLogLevel.Info, "No correction needed for captured text");
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
            var result = _pipeline.CorrectNowAsync(sample).Result;
            if (result == null)
            {
                failures.Add("Pipeline returned null");
            }
            else
            {
                var checks = new[]
                {
                    ("receive", result.Corrected.Contains("receive", StringComparison.OrdinalIgnoreCase)),
                    ("friend", result.Corrected.Contains("friend", StringComparison.OrdinalIgnoreCase)),
                    ("definitely", result.Corrected.Contains("definitely", StringComparison.OrdinalIgnoreCase))
                };

                foreach (var check in checks)
                {
                    if (check.Item2)
                        DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"Self-test PASS: contains '{check.Item1}'");
                    else
                    {
                        failures.Add(check.Item1);
                        DiagnosticLogger.Log(DiagnosticLogLevel.Error, $"Self-test FAIL: missing '{check.Item1}'");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add(ex.Message);
            DiagnosticLogger.Log(DiagnosticLogLevel.Error, $"Self-test exception: {ex.Message}");
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            if (failures.Count == 0)
            {
                MessageBox.Show("Self-test: 3/3 passed ✓", "GrammarFixer Self-Test",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                var details = string.Join(Environment.NewLine, failures.Select(x => $"- {x}"));
                MessageBox.Show($"Self-test failed:{Environment.NewLine}{details}", "GrammarFixer Self-Test",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private void OnCorrectionReady(CorrectionResult result)
    {
        if (!_settings.Enabled) return;
        if (result.Corrected == result.Original) return;
        
        _lastResult = result;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var pos = UiaHelper.GetCaretScreenPosition();
            _floatingBtn?.ShowAt(pos);
        });
    }

    private void ShowOverlay(CorrectionResult result)
    {
        _overlay?.Close();
        var pos = UiaHelper.GetCaretScreenPosition();
        _overlay = new OverlayWindow(result, pos, this);
        _overlay.Show();
    }

    public void ApplyCorrection(CorrectionResult result)
    {
        UiaHelper.SetFocusedText(result.Corrected);
        _overlay?.Close();
        _overlay = null;
    }

    public void DismissOverlay()
    {
        _overlay?.Close();
        _overlay = null;
    }

    public void ApplyCorrectionFromWindow(string correctedText)
    {
        UiaHelper.SetFocusedText(correctedText);
    }

    private bool IsAppAllowed(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Warn, "Process check failed: no process name");
            return false;
        }

        var deniedMatch = _settings.DeniedApps.FirstOrDefault(d =>
            processName.Contains(d, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(deniedMatch))
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Info,
                $"Process '{processName}' blocked by DeniedApps match '{deniedMatch}'");
            return false;
        }

        if (_settings.AllowedApps.Count == 0)
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Debug,
                $"Process '{processName}' allowed (AllowedApps empty, no DeniedApps match)");
            return true;
        }

        var allowedMatch = _settings.AllowedApps.FirstOrDefault(a =>
            processName.Contains(a, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(allowedMatch))
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Debug,
                $"Process '{processName}' allowed by AllowedApps match '{allowedMatch}'");
            return true;
        }

        DiagnosticLogger.Log(DiagnosticLogLevel.Info,
            $"Process '{processName}' blocked (no AllowedApps match)");
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