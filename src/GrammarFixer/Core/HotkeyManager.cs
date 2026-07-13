using System.Windows.Input;

namespace GrammarFixer.Core;

/// <summary>
/// Watches keyboard hook KeyDown events and fires when the configured hotkey is detected.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly KeyboardHook _hook;
    private Key _triggerKey;
    private bool _ctrlDown;
    private bool _altDown;

    public event Action? HotkeyPressed;

    public HotkeyManager(string hotkey = "Ctrl+Alt+G")
    {
        ParseHotkey(hotkey);
        _hook = new KeyboardHook();
        _hook.KeyDown += OnKeyDown;
    }

    public void Start() => _hook.Install();
    public void Stop()  => _hook.Uninstall();

    public void UpdateHotkey(string hotkey) => ParseHotkey(hotkey);

    private void ParseHotkey(string hotkey)
    {
        var parts = hotkey.Split('+');
        _triggerKey = Enum.Parse<Key>(parts[^1], ignoreCase: true);
    }

    private void OnKeyDown(Key key)
    {
        if (key is Key.LeftCtrl  or Key.RightCtrl) _ctrlDown = true;
        if (key is Key.LeftAlt   or Key.RightAlt)  _altDown  = true;

        if (_ctrlDown && _altDown && key == _triggerKey)
            HotkeyPressed?.Invoke();

        // Reset on non-modifier keys (prevents stuck state)
        if (key is not (Key.LeftCtrl or Key.RightCtrl or
                        Key.LeftAlt  or Key.RightAlt  or
                        Key.LeftShift or Key.RightShift))
        {
            _ctrlDown = false;
            _altDown  = false;
        }
    }

    public void Dispose()
    {
        _hook.KeyDown -= OnKeyDown;
        _hook.Dispose();
    }
}
