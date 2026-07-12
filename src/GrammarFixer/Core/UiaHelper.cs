using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace GrammarFixer.Core;

/// <summary>
/// UI Automation helper for reading and writing text in arbitrary Windows apps.
///
/// Strategy:
/// 1. Try ValuePattern.SetValue (Win32, WPF, UWP, most apps)
/// 2. Fall back to TextPattern for reading selection
/// 3. Fall back to clipboard (Ctrl+A, Ctrl+C / Ctrl+V) for Electron apps
///
/// Reference: https://stackoverflow.com/a/75229144 (TextPattern selection replace)
/// Microsoft docs: https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-textpattern-overview
/// </summary>
public static class UiaHelper
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCaretPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    /// <summary>Gets the text content of the currently focused element.</summary>
    public static string? GetFocusedText()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return null;

            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
                return ((ValuePattern)vp).Current.Value;

            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out var tp))
                return ((TextPattern)tp).DocumentRange.GetText(-1);

            return null;
        }
        catch { return null; }
    }

    /// <summary>
    /// Replaces text in the focused element.
    /// Tries ValuePattern first, then clipboard fallback.
    /// </summary>
    public static bool SetFocusedText(string newText)
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return false;

            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
            {
                ((ValuePattern)vp).SetValue(newText);
                return true;
            }

            // Clipboard fallback for Electron / Chromium apps
            return SetViaClipboard(newText);
        }
        catch
        {
            return SetViaClipboard(newText);
        }
    }

    /// <summary>
    /// Gets the screen position for the suggestion overlay.
    /// Tries UIA BoundingRectangle first, then GetCaretPos Win32 fallback.
    /// </summary>
    public static Point GetCaretScreenPosition()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused != null)
            {
                var rect = focused.Current.BoundingRectangle;
                if (!rect.IsEmpty)
                    return new Point(rect.Left, rect.Bottom + 4);
            }
        }
        catch { }

        // Win32 fallback
        var hwnd = GetForegroundWindow();
        if (GetCaretPos(out var pt))
        {
            ClientToScreen(hwnd, ref pt);
            return new Point(pt.X, pt.Y + 20);
        }

        return new Point(100, 100);
    }

    /// <summary>Returns the process name of the foreground window's owner.</summary>
    public static string? GetForegroundProcessName()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            GetWindowThreadProcessId(hwnd, out var pid);
            return Process.GetProcessById((int)pid).ProcessName;
        }
        catch { return null; }
    }

    private static bool SetViaClipboard(string text)
    {
        try
        {
            var prev = Clipboard.GetText();
            Clipboard.SetText(text);
            // Select all + paste
            SendKey(System.Windows.Forms.Keys.Control, System.Windows.Forms.Keys.A);
            System.Threading.Thread.Sleep(30);
            SendKey(System.Windows.Forms.Keys.Control, System.Windows.Forms.Keys.V);
            System.Threading.Thread.Sleep(30);
            return true;
        }
        catch { return false; }
    }

    private static void SendKey(params System.Windows.Forms.Keys[] keys)
    {
        var inputs = new System.Windows.Forms.Keys[keys.Length];
        System.Windows.Forms.SendKeys.SendWait(
            string.Concat(keys.Select(k => k switch
            {
                System.Windows.Forms.Keys.Control => "^",
                System.Windows.Forms.Keys.A => "a",
                System.Windows.Forms.Keys.V => "v",
                _ => ""
            })));
    }
}
