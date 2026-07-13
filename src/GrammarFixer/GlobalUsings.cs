// ============================================================
// Global using aliases — resolve WPF vs WinForms ambiguities
// caused by <UseWindowsForms>true</UseWindowsForms>.
// One file fixes every CS0104 across the whole project.
// ============================================================

// WPF types we always want by short name
global using WpfApp        = System.Windows.Application;
global using WpfPoint      = System.Windows.Point;
global using WpfColor      = System.Windows.Media.Color;
global using WpfColors     = System.Windows.Media.Colors;
global using WpfMessageBox = System.Windows.MessageBox;
global using WpfMouseArgs  = System.Windows.Input.MouseEventArgs;

// Explicit full-qualification aliases for WinForms — only used in UiaHelper
global using FormsKeys     = System.Windows.Forms.Keys;
global using FormsSendKeys = System.Windows.Forms.SendKeys;
global using FormsCursor   = System.Windows.Forms.Cursor;
global using FormsTimer    = System.Windows.Forms.Timer;
