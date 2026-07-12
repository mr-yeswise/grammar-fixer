using System.Windows.Input;

namespace GrammarFixer.Core;

/// <summary>
/// Watches keyboard hook events and fires when the configured hotkey combination is detected.
/// Default: Ctrl+Alt+G
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly KeyboardHook _hook;
    private readonly Key _triggerKey;
    private bool _ctrlDown;
    private bool _altDown;

    public event Action? HotkeyPressed;

    public HotkeyManager(string hotkey = "Ctrl+Alt+G")
    {
        // Parse "Ctrl+Alt+G" format
        var parts = hotkey.Split('+');
        _triggerKey = Enum.Parse<Key>(parts[^1], ignoreCase: true);

        _hook = new KeyboardHook();
        _hook.KeyDown += OnKeyDown;
    }

    public void Start() => _hook.Install();
    public void Stop() => _hook.Uninstall();

    private void OnKeyDown(Key key)
    {
        if (key == Key.LeftCtrl || key == Key.RightCtrl) _ctrlDown = true;
        if (key == Key.LeftAlt || key == Key.RightAlt) _altDown = true;

        if (_ctrlDown && _altDown && key == _triggerKey)
            HotkeyPressed?.Invoke();

        // Reset modifiers on non-modifier key
        if (key != Key.LeftCtrl && key != Key.RightCtrl &&
            key != Key.LeftAlt && key != Key.RightAlt &&
            key != Key.LeftShift && key != Key.RightShift &&
            key != _triggerKey)
        {
            _ctrlDown = false;
            _altDown = false;
        }
    }

    public void Dispose()
    {
        _hook.KeyDown -= OnKeyDown;
        _hook.Dispose();
    }
}
