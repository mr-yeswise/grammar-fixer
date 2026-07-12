using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using GrammarFixer.Core;
using GrammarFixer.Models;

namespace GrammarFixer.UI;

/// <summary>
/// Always-on-top borderless WPF suggestion overlay.
/// Uses DiffPlex for word-level diff rendering.
/// Appears near caret via UiaHelper.GetCaretScreenPosition().
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly CorrectionResult _result;
    private readonly AppController _controller;

    public OverlayWindow(CorrectionResult result, Point caretPos, AppController controller)
    {
        InitializeComponent();
        _result = result;
        _controller = controller;

        Left = caretPos.X;
        Top = caretPos.Y;

        // Clamp to screen working area
        var screen = SystemParameters.WorkArea;
        if (Left + 460 > screen.Right) Left = screen.Right - 464;
        if (Left < screen.Left) Left = screen.Left + 4;
        if (Top + 160 > screen.Bottom) Top = caretPos.Y - 168;
        if (Top < screen.Top) Top = screen.Top + 4;

        RenderDiff(result.Original, result.Corrected);
    }

    private void RenderDiff(string original, string corrected)
    {
        var differ = new InlineDiffBuilder(new Differ());
        var diff = differ.BuildDiffModel(original, corrected, ignoreWhitespace: false);

        var doc = new FlowDocument { FontSize = 13, PageWidth = 400 };
        var para = new Paragraph { LineHeight = 20 };

        foreach (var line in diff.Lines)
        {
            switch (line.Type)
            {
                case ChangeType.Inserted:
                    para.Inlines.Add(new Run(" " + line.Text + " ")
                    {
                        Background = new SolidColorBrush(Color.FromRgb(198, 239, 206)),
                        Foreground = new SolidColorBrush(Color.FromRgb(0, 97, 0)),
                        FontWeight = FontWeights.SemiBold
                    });
                    break;
                case ChangeType.Deleted:
                    para.Inlines.Add(new Run(" " + line.Text + " ")
                    {
                        Background = new SolidColorBrush(Color.FromRgb(255, 199, 206)),
                        Foreground = new SolidColorBrush(Color.FromRgb(156, 0, 6)),
                        TextDecorations = TextDecorations.Strikethrough
                    });
                    break;
                default:
                    para.Inlines.Add(new Run(line.Text));
                    break;
            }
        }

        doc.Blocks.Add(para);
        DiffBox.Document = doc;
    }

    private void AcceptAll_Click(object sender, RoutedEventArgs e)
    {
        _controller.ApplyCorrection(_result);
        Close();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        _controller.DismissOverlay();
        Close();
    }
}
