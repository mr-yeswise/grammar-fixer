using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using GrammarFixer.Services;

namespace GrammarFixer.Core;

/// <summary>
/// UI Automation helper for reading and writing text in arbitrary Windows apps.
///
/// Strategies (in priority order):
/// 1. TextPattern.GetSelection() — selection-aware, works in most native + WPF apps
/// 2. ValuePattern.SetValue / GetValue — Win32, WPF, UWP
/// 3. Clipboard fallback (Ctrl+C / Ctrl+V) — Electron apps (VS Code, Slack, Discord)
///
/// KEY RULE: GetSelectedText / ReplaceSelectedText NEVER send Ctrl+A.
///           They only operate on the current selection.
/// </summary>
public static class UiaHelper
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint   GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCaretPos(out POINT pt);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT pt);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    // ── SELECTION ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets ONLY the currently selected text in the focused element.
    /// Adds a short delay so hotkey modifier keys are fully released before reading.
    /// </summary>
    public static string? GetSelectedText()
    {
        // Wait for Ctrl+Alt+G keys to physically release so they don't
        // interfere with the clipboard Ctrl+C fallback.
        System.Threading.Thread.Sleep(120);

        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused != null && focused.TryGetCurrentPattern(TextPattern.Pattern, out var tp))
            {
                var ranges = ((TextPattern)tp).GetSelection();
                if (ranges.Length > 0)
                {
                    var text = ranges[0].GetText(-1);
                    if (!string.IsNullOrEmpty(text))
                    {
                        DiagnosticLogger.Log(DiagnosticLogLevel.Info,
                            $"UIA: GetSelectedText via TextPattern, length={text.Length}");
                        return text;
                    }
                }
            }
        }
        catch { }

        // Clipboard fallback — Ctrl+C only (no Ctrl+A)
        return ReadSelectionViaClipboard();
    }

    /// <summary>
    /// Replaces ONLY the current selection with newText.
    /// Does NOT send Ctrl+A — pastes directly over the active selection.
    /// </summary>
    public static bool ReplaceSelectedText(string newText)
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused != null && focused.TryGetCurrentPattern(TextPattern.Pattern, out var tp))
            {
                var ranges = ((TextPattern)tp).GetSelection();
                if (ranges.Length > 0)
                {
                    ranges[0].Select(); // ensure selection is still active
                    return PasteViaClipboard(newText);
                }
            }
        }
        catch { }

        return PasteViaClipboard(newText);
    }

    // ── FULL FIELD ───────────────────────────────────────────────────────────────────

    /// <summary>Gets the full text of the currently focused UI element.</summary>
    public static string? GetFocusedText()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return null;
            var name = focused.Current.Name;

            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
            {
                var val = ((ValuePattern)vp).Current.Value;
                if (!string.IsNullOrEmpty(val))
                {
                    DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"UIA: GetFocusedText via ValuePattern, element={name}, length={val.Length}");
                    return val;
                }
            }

            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out var tp))
            {
                var txt = ((TextPattern)tp).DocumentRange.GetText(-1);
                if (!string.IsNullOrEmpty(txt))
                {
                    DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"UIA: GetFocusedText via TextPattern, element={name}, length={txt.Length}");
                    return txt;
                }
            }

            var clip = ReadViaClipboard();
            DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"UIA: GetFocusedText via Clipboard, element={name}, length={clip?.Length ?? 0}");
            return clip;
        }
        catch { return null; }
    }

    /// <summary>Replaces the full text of the focused element.</summary>
    public static bool SetFocusedText(string newText)
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null) return SetViaClipboard(newText);

            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
            {
                var vPattern = (ValuePattern)vp;
                if (!vPattern.Current.IsReadOnly)
                {
                    vPattern.SetValue(newText);
                    DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"UIA: SetFocusedText via ValuePattern, length={newText.Length}");
                    return true;
                }
            }

            return SetViaClipboard(newText);
        }
        catch { return SetViaClipboard(newText); }
    }

    // ── CARET ───────────────────────────────────────────────────────────────────────

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

        var mouse = System.Windows.Forms.Cursor.Position;
        return new System.Windows.Point(mouse.X, mouse.Y + 20);
    }

    // ── PROCESS ─────────────────────────────────────────────────────────────────────

    public static string? GetForegroundProcessName()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
            return Process.GetProcessById((int)pid).ProcessName;
        }
        catch { return null; }
    }

    // ── PRIVATE CLIPBOARD HELPERS ───────────────────────────────────────────────

    private static string? ReadSelectionViaClipboard()
    {
        try
        {
            string? prev = null;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                prev = System.Windows.Clipboard.GetText());

            System.Windows.Forms.SendKeys.SendWait("^c"); // copy selection only, NO Ctrl+A
            System.Threading.Thread.Sleep(100);

            string? captured = null;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                captured = System.Windows.Clipboard.GetText());

            // Restore previous clipboard content
            if (!string.IsNullOrEmpty(prev))
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    System.Windows.Clipboard.SetText(prev));

            DiagnosticLogger.Log(DiagnosticLogLevel.Info,
                $"UIA: GetSelectedText via Clipboard, length={captured?.Length ?? 0}");
            return string.IsNullOrEmpty(captured) ? null : captured;
        }
        catch { return null; }
    }

    private static string? ReadViaClipboard()
    {
        try
        {
            string? prev = null;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                prev = System.Windows.Clipboard.GetText());

            System.Windows.Forms.SendKeys.SendWait("^a");
            System.Threading.Thread.Sleep(60);
            System.Windows.Forms.SendKeys.SendWait("^c");
            System.Threading.Thread.Sleep(80);

            string? captured = null;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                captured = System.Windows.Clipboard.GetText());

            if (!string.IsNullOrEmpty(prev))
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    System.Windows.Clipboard.SetText(prev));

            return string.IsNullOrEmpty(captured) ? null : captured;
        }
        catch { return null; }
    }

    private static bool PasteViaClipboard(string text)
    {
        try
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                System.Windows.Clipboard.SetText(text));
            System.Windows.Forms.SendKeys.SendWait("^v"); // paste over selection, NO Ctrl+A
            System.Threading.Thread.Sleep(30);
            DiagnosticLogger.Log(DiagnosticLogLevel.Info, $"UIA: PasteViaClipboard, length={text.Length}");
            return true;
        }
        catch { return false; }
    }

    private static bool SetViaClipboard(string text)
    {
        try
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                System.Windows.Clipboard.SetText(text));
            System.Windows.Forms.SendKeys.SendWait("^a");
            System.Threading.Thread.Sleep(30);
            System.Windows.Forms.SendKeys.SendWait("^v");
            System.Threading.Thread.Sleep(30);
            return true;
        }
        catch { return false; }
    }
}
