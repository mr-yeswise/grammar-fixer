using System.Windows.Input;

namespace GrammarFixer.Core;

/// <summary>
/// Watches keyboard hook events and fires when the configured hotkey combination is detected.
/// Default: Ctrl+Alt+G
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly KeyboardHook _hook;
    private Key _triggerKey;
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

    /// <summary>Swap the hotkey at runtime (called from AppController.UpdateSettings).</summary>
    public void UpdateHotkey(string hotkey) => ParseHotkey(hotkey);

    private void ParseHotkey(string hotkey)
    {
        var parts = hotkey.Split('+');
        _triggerKey = Enum.Parse<Key>(parts[^1], ignoreCase: true);
    }

    private void OnKeyDown(Key key)
    {
        if (key is Key.LeftCtrl  or Key.RightCtrl)  _ctrlDown  = true;
        if (key is Key.LeftAlt   or Key.RightAlt)   _altDown   = true;
        if (key is Key.LeftShift or Key.RightShift) _shiftDown = true;

        if (_ctrlDown && _altDown && key == _triggerKey)
            HotkeyPressed?.Invoke();
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
