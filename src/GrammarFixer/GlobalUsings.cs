// ============================================================
// Global using aliases — resolve WPF vs WinForms ambiguities
// One file fixes every CS0104 across the whole project.
// ============================================================
global using WpfApp        = System.Windows.Application;
global using WpfPoint      = System.Windows.Point;
global using WpfColor      = System.Windows.Media.Color;
global using WpfColors     = System.Windows.Media.Colors;
global using WpfMessageBox = System.Windows.MessageBox;
global using WpfMouseArgs  = System.Windows.Input.MouseEventArgs;
global using WpfClipboard  = System.Windows.Clipboard;

// WinForms — only used in UiaHelper
global using FormsKeys     = System.Windows.Forms.Keys;
global using FormsSendKeys = System.Windows.Forms.SendKeys;
global using FormsCursor   = System.Windows.Forms.Cursor;
global using FormsTimer    = System.Windows.Forms.Timer;
