using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfPoint = System.Windows.Point;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using GrammarFixer.Core;

namespace GrammarFixer.UI;

/// <summary>
/// Quillbot-style floating pill button that appears near the caret
/// after the user pauses typing. Clicking it triggers correction.
/// Auto-hides after 4 seconds of inactivity.
/// </summary>
public partial class FloatingButton : Window
{
    private readonly AppController _controller;
    private readonly System.Windows.Threading.DispatcherTimer _hideTimer;
    private bool _isHovered;

    public FloatingButton(AppController controller)
    {
        InitializeComponent();
        _controller = controller;

        _hideTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        _hideTimer.Tick += (_, _) =>
        {
            if (!_isHovered) FadeOut();
        };
    }

    /// <summary>Show the pill at a screen position (near caret).</summary>
    public void ShowAt(WpfPoint screenPos)
    {
        Left = screenPos.X + 4;
        Top  = screenPos.Y + 6;

        var wa = SystemParameters.WorkArea;
        if (Left + 80  > wa.Right)  Left = wa.Right  - 84;
        if (Left       < wa.Left)   Left = wa.Left   + 4;
        if (Top  + 40  > wa.Bottom) Top  = screenPos.Y - 38;
        if (Top        < wa.Top)    Top  = wa.Top    + 4;

        Opacity = 0;
        if (!IsVisible) Show();
        FadeIn();

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    public void ResetTimer()
    {
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void FadeIn()
    {
        var anim = new DoubleAnimation(0, 0.92, TimeSpan.FromMilliseconds(180));
        BeginAnimation(OpacityProperty, anim);
    }

    private void FadeOut()
    {
        var anim = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(220));
        anim.Completed += (_, _) =>
        {
            Hide();
            Opacity = 0.92;
        };
        BeginAnimation(OpacityProperty, anim);
    }

    private void Pill_Click(object sender, MouseButtonEventArgs e)
    {
        _hideTimer.Stop();
        FadeOut();
        _controller.TriggerFromFloatingButton();
    }

    private void Pill_MouseEnter(object sender, WpfMouseEventArgs e)
    {
        _isHovered = true;
        _hideTimer.Stop();
        PillBorder.Background = new SolidColorBrush(Color.FromRgb(72, 63, 200));
    }

    private void Pill_MouseLeave(object sender, WpfMouseEventArgs e)
    {
        _isHovered = false;
        PillBorder.Background = new SolidColorBrush(Color.FromRgb(91, 79, 232));
        _hideTimer.Start();
    }
}
