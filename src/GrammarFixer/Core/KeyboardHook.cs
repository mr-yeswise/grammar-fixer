using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using GrammarFixer.Services;

namespace GrammarFixer.Core;

/// <summary>
/// Low-level global keyboard hook (WH_KEYBOARD_LL).
/// Fires KeyDown on WM_KEYDOWN/WM_SYSKEYDOWN and KeyUp on WM_KEYUP/WM_SYSKEYUP.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL  = 13;
    private const int WM_KEYDOWN      = 0x0100;
    private const int WM_SYSKEYDOWN   = 0x0104;
    private const int WM_KEYUP        = 0x0101;
    private const int WM_SYSKEYUP     = 0x0105;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private bool _disposed;

    public event Action<Key>? KeyDown;
    public event Action<Key>? KeyUp;

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Install()
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule  = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        if (_hookId == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to set keyboard hook. Error: {Marshal.GetLastWin32Error()}");
        DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"Hook installed, handle={_hookId}");
    }

    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"Hook uninstalled, handle={_hookId}");
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kb  = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var key = KeyInterop.KeyFromVirtualKey((int)kb.vkCode);

            if (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN)
                KeyDown?.Invoke(key);
            else if (wParam == WM_KEYUP || wParam == WM_SYSKEYUP)
                KeyUp?.Invoke(key);
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Uninstall();
        _disposed = true;
    }
}
