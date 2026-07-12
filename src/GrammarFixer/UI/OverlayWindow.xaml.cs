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
/// Always-on-top borderless WPF overlay that appears near the caret.
/// Uses DiffPlex for word-level diff rendering.
/// Positioned via UiaHelper.GetCaretScreenPosition().
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

        // Clamp to screen bounds
        var screen = SystemParameters.WorkArea;
        if (Left + 450 > screen.Right) Left = screen.Right - 450;
        if (Top + 120 > screen.Bottom) Top = caretPos.Y - 130;

        RenderDiff(result.Original, result.Corrected);
    }

    private void RenderDiff(string original, string corrected)
    {
        var differ = new InlineDiffBuilder(new Differ());
        var diff = differ.BuildDiffModel(original, corrected, ignoreWhitespace: false);

        var para = new Paragraph();
        para.LineHeight = 18;

        foreach (var line in diff.Lines)
        {
            switch (line.Type)
            {
                case ChangeType.Inserted:
                    para.Inlines.Add(new Run(line.Text)
                    {
                        Background = Brushes.LightGreen,
                        FontWeight = FontWeights.SemiBold
                    });
                    break;
                case ChangeType.Deleted:
                    para.Inlines.Add(new Run(line.Text)
                    {
                        Background = Brushes.LightCoral,
                        TextDecorations = TextDecorations.Strikethrough,
                        Foreground = Brushes.DarkRed
                    });
                    break;
                default:
                    para.Inlines.Add(new Run(line.Text));
                    break;
            }
        }

        var doc = new FlowDocument(para)
        {
            PageWidth = 420,
            FontSize = 13
        };
        var rtb = new System.Windows.Controls.RichTextBox(doc)
        {
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            MaxWidth = 420,
            MaxHeight = 100
        };

        // Replace the TextBlock with the rich diff view
        if (DiffTextBlock.Parent is System.Windows.Controls.Panel panel)
        {
            var idx = panel.Children.IndexOf(DiffTextBlock);
            panel.Children.Remove(DiffTextBlock);
            panel.Children.Insert(idx, rtb);
        }
    }

    private void AcceptAll_Click(object sender, RoutedEventArgs e)
    {
        _controller.ApplyCorrection(_result);
        Close();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        _controller.DismissOverlay();
    }
}
