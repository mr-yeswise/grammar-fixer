using System.Windows.Input;
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
///   -> CorrectionPipeline corrects
///   -> show FloatingButton pill near caret
///   -> on pill click OR Ctrl+Alt+G: apply correction or show overlay
/// </summary>
public sealed class AppController : IDisposable
{
    private AppSettings _settings;
    private readonly CorrectionPipeline _pipeline;
    private readonly HotkeyManager _hotkey;
    private readonly KeyboardHook _typingHook;
    private OverlayWindow? _overlay;
    private FloatingButton? _floatingBtn;
    private bool _disposed;

    private string? _lastCapturedText;
    private CorrectionResult? _lastResult;

    private readonly System.Timers.Timer _typingDebounce;

    public AppSettings Settings => _settings;

    public AppController(AppSettings settings)
    {
        _settings = settings;
        _pipeline = new CorrectionPipeline(settings);
        _pipeline.CorrectionReady += OnCorrectionReady;

        _hotkey = new HotkeyManager(settings.HotkeyTrigger);
        _hotkey.HotkeyPressed += OnHotkeyPressed;

        _typingHook = new KeyboardHook();
        _typingHook.KeyDown += OnTypingKeyDown;

        _typingDebounce = new System.Timers.Timer(settings.DebounceMs) { AutoReset = false };
        _typingDebounce.Elapsed += async (_, _) => await OnTypingPaused();
    }

    public void Start()
    {
        _hotkey.Start();
        _typingHook.Install();

        WpfApp.Current.Dispatcher.Invoke(() =>
        {
            _floatingBtn = new FloatingButton(this);
        });
    }

    public void Stop()
    {
        _hotkey.Stop();
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

        WpfApp.Current.Dispatcher.Invoke(() =>
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

        if (key == Key.LeftCtrl  || key == Key.RightCtrl  ||
            key == Key.LeftAlt   || key == Key.RightAlt   ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LWin      || key == Key.RWin) return;

        _typingDebounce.Stop();
        _typingDebounce.Start();
    }

    private async Task OnTypingPaused()
    {
        if (!_settings.Enabled) return;

        string? processName = null;
        string? text        = null;
        WpfPoint caretPos   = default;

        await WpfApp.Current.Dispatcher.InvokeAsync(() =>
        {
            processName = UiaHelper.GetForegroundProcessName();
            if (!IsAppAllowed(processName)) return;
            text      = UiaHelper.GetFocusedText();
            caretPos  = UiaHelper.GetCaretScreenPosition();
        });

        if (string.IsNullOrWhiteSpace(text)) return;
        if (!IsAppAllowed(processName)) return;

        _lastCapturedText = text;
        _lastResult       = null;

        var result = await _pipeline.CorrectNowAsync(text);
        if (result != null && result.Corrected != result.Original)
        {
            _lastResult = result;
            WpfApp.Current.Dispatcher.Invoke(() =>
            {
                _floatingBtn?.ShowAt(caretPos);
            });
        }
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

    private bool IsAppAllowed(string? processName)
    {
        if (processName == null) return false;
        if (_settings.DeniedApps.Any(d =>
            processName.Contains(d, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (_settings.AllowedApps.Count == 0) return true;
        return _settings.AllowedApps.Any(a =>
            processName.Contains(a, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _hotkey.Dispose();
        _typingHook.Dispose();
        _typingDebounce.Dispose();
        _pipeline.Dispose();
        _disposed = true;
    }
}
