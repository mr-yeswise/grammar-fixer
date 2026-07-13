using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Forms;
using GrammarFixer.Services;

namespace GrammarFixer.Core;

/// <summary>
/// UI Automation helper for reading and writing text in arbitrary Windows apps.
///
/// Strategy:
/// 1. ValuePattern.SetValue  — Win32, WPF, UWP, most apps
/// 2. TextPattern             — read-only fallback for apps that only expose TextPattern
/// 3. Clipboard               — Ctrl+A / Ctrl+V for Electron (VS Code, Slack, Discord, WhatsApp)
///
/// Caret position: UIA BoundingRectangle → Win32 GetCaretPos fallback.
///
/// Reference: https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-textpattern-overview
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

    /// <summary>Gets the full text of the currently focused UI element.</summary>
    public static string? GetFocusedText()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return null;
            var elementName = focused.Current.Name;

            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
            {
                var val = ((ValuePattern)vp).Current.Value;
                if (!string.IsNullOrEmpty(val))
                {
                    DiagnosticLogger.Log(DiagnosticLogLevel.Info,
                        $"UIA: strategy=ValuePattern, element={elementName}, text length={val.Length}");
                    return val;
                }
            }

            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out var tp))
            {
                var txt = ((TextPattern)tp).DocumentRange.GetText(-1);
                if (!string.IsNullOrEmpty(txt))
                {
                    DiagnosticLogger.Log(DiagnosticLogLevel.Info,
                        $"UIA: strategy=TextPattern, element={elementName}, text length={txt.Length}");
                    return txt;
                }
            }

            // Clipboard read fallback (Ctrl+A, Ctrl+C)
            var clipboardText = ReadViaClipboard();
            DiagnosticLogger.Log(DiagnosticLogLevel.Info,
                $"UIA: strategy=Clipboard, element={elementName}, text length={clipboardText?.Length ?? 0}");
            return clipboardText;
        }
        catch { return null; }
    }

    /// <summary>Replaces the focused element's text with newText.</summary>
    public static bool SetFocusedText(string newText)
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null)
            {
                var ok = SetViaClipboard(newText);
                DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"UIA: set text via Clipboard, length={newText.Length}");
                return ok;
            }

            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
            {
                var vPattern = (ValuePattern)vp;
                if (!vPattern.Current.IsReadOnly)
                {
                    vPattern.SetValue(newText);
                    DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"UIA: set text via ValuePattern, length={newText.Length}");
                    return true;
                }
            }

            var setViaClipboard = SetViaClipboard(newText);
            DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"UIA: set text via Clipboard, length={newText.Length}");
            return setViaClipboard;
        }
        catch
        {
            var fallbackSet = SetViaClipboard(newText);
            DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"UIA: set text via Clipboard, length={newText.Length}");
            return fallbackSet;
        }
    }

    /// <summary>Returns screen position for the floating button / overlay.</summary>
    public static System.Windows.Point GetCaretScreenPosition()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused != null)
            {
                var rect = focused.Current.BoundingRectangle;
                if (!rect.IsEmpty && rect.Width > 0)
                    return new System.Windows.Point(rect.Right - 90, rect.Bottom + 6);
            }
        }
        catch { }

        var hwnd = GetForegroundWindow();
        if (GetCaretPos(out var pt))
        {
            ClientToScreen(hwnd, ref pt);
            return new System.Windows.Point(pt.X + 4, pt.Y + 20);
        }

        // Last resort: mouse cursor position
        var mouse = System.Windows.Forms.Cursor.Position;
        return new System.Windows.Point(mouse.X, mouse.Y + 20);
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

    private static string? ReadViaClipboard()
    {
        try
        {
            var prev = System.Windows.Clipboard.GetText();
            System.Windows.Forms.SendKeys.SendWait("^a");
            System.Threading.Thread.Sleep(60);
            System.Windows.Forms.SendKeys.SendWait("^c");
            System.Threading.Thread.Sleep(80);
            var captured = System.Windows.Clipboard.GetText();
            // Restore original clipboard
            if (!string.IsNullOrEmpty(prev))
                System.Windows.Clipboard.SetText(prev);
            return string.IsNullOrEmpty(captured) ? null : captured;
        }
        catch { return null; }
    }

    private static bool SetViaClipboard(string text)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
            System.Windows.Forms.SendKeys.SendWait("^a");
            System.Threading.Thread.Sleep(30);
            System.Windows.Forms.SendKeys.SendWait("^v");
            System.Threading.Thread.Sleep(30);
            return true;
        }
        catch { return false; }
    }
}
