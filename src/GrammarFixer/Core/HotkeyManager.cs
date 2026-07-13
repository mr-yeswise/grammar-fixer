using System.Windows.Input;

namespace GrammarFixer.Core;

/// <summary>
/// Watches keyboard hook KeyDown events and fires when the configured hotkey combo is detected.
/// Tracks Ctrl, Alt, Shift modifier state correctly across key sequences.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly KeyboardHook _hook;
    private Key  _triggerKey;
    private bool _needCtrl;
    private bool _needAlt;
    private bool _needShift;
    private bool _ctrlDown;
    private bool _altDown;
    private bool _shiftDown;

    public event Action? HotkeyPressed;

    public HotkeyManager(string hotkey = "Ctrl+Alt+G")
    {
        ParseHotkey(hotkey);
        _hook = new KeyboardHook();
        _hook.KeyDown += OnKeyDown;
        _hook.KeyUp   += OnKeyUp;
    }

    public void Start() => _hook.Install();
    public void Stop()  => _hook.Uninstall();
    public void UpdateHotkey(string hotkey) => ParseHotkey(hotkey);

    private void ParseHotkey(string hotkey)
    {
        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries);
        _needCtrl  = parts.Any(p => p.Equals("Ctrl",  StringComparison.OrdinalIgnoreCase));
        _needAlt   = parts.Any(p => p.Equals("Alt",   StringComparison.OrdinalIgnoreCase));
        _needShift = parts.Any(p => p.Equals("Shift", StringComparison.OrdinalIgnoreCase));
        _triggerKey = Enum.Parse<Key>(parts[^1], ignoreCase: true);
    }

    private void OnKeyDown(Key key)
    {
        // Track modifier state
        if (key is Key.LeftCtrl  or Key.RightCtrl)  { _ctrlDown  = true; return; }
        if (key is Key.LeftAlt   or Key.RightAlt)   { _altDown   = true; return; }
        if (key is Key.LeftShift or Key.RightShift) { _shiftDown = true; return; }

        // Check trigger
        if (key == _triggerKey
            && (!_needCtrl  || _ctrlDown)
            && (!_needAlt   || _altDown)
            && (!_needShift || _shiftDown))
        {
            HotkeyPressed?.Invoke();
        }
    }

    private void OnKeyUp(Key key)
    {
        if (key is Key.LeftCtrl  or Key.RightCtrl)  _ctrlDown  = false;
        if (key is Key.LeftAlt   or Key.RightAlt)   _altDown   = false;
        if (key is Key.LeftShift or Key.RightShift) _shiftDown = false;
    }

    public void Dispose()
    {
        _hook.KeyDown -= OnKeyDown;
        _hook.KeyUp   -= OnKeyUp;
        _hook.Dispose();
    }
}
