using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using IMTReader.Core.Text;

namespace IMTReader.App;

/// <summary>把 MarkdownLite 解析结果渲染为 FlowDocument（笔记阅读模式）。</summary>
public static class MarkdownRenderer
{
    public static FlowDocument Render(string markdown, FontFamily font, double fontSize, Brush foreground)
    {
        var doc = new FlowDocument
        {
            FontFamily = font,
            FontSize = fontSize,
            Foreground = foreground,
            PagePadding = new Thickness(18),
            LineHeight = fontSize * 1.7
        };
        var mark = new SolidColorBrush(Color.FromRgb(255, 241, 118));
        mark.Freeze();
        foreach (var block in MarkdownLite.Parse(markdown))
        {
            switch (block)
            {
                case MdHeading h:
                {
                    var p = new Paragraph { FontWeight = FontWeights.Bold, Margin = new Thickness(0, fontSize * 0.6, 0, fontSize * 0.3) };
                    p.FontSize = h.Level switch { 1 => fontSize * 1.6, 2 => fontSize * 1.35, 3 => fontSize * 1.15, _ => fontSize * 1.05 };
                    AddSpans(p, h.Spans, mark);
                    doc.Blocks.Add(p);
                    break;
                }
                case MdParagraph para:
                {
                    var p = new Paragraph { Margin = new Thickness(0, 0, 0, fontSize * 0.5) };
                    AddSpans(p, para.Spans, mark);
                    doc.Blocks.Add(p);
                    break;
                }
                case MdListItem li:
                {
                    var p = new Paragraph { Margin = new Thickness(fontSize * (1.2 + li.Indent * 1.2), 0, 0, fontSize * 0.2), TextIndent = -fontSize * 1.1 };
                    p.Inlines.Add(new Run(li.Marker + " "));
                    AddSpans(p, li.Spans, mark);
                    doc.Blocks.Add(p);
                    break;
                }
                case MdQuote q:
                {
                    var p = new Paragraph { Margin = new Thickness(fontSize, 0, 0, fontSize * 0.5), Foreground = Brushes.Gray, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(10, 0, 0, 0) };
                    AddSpans(p, q.Spans, mark);
                    doc.Blocks.Add(p);
                    break;
                }
                case MdCode c:
                {
                    var p = new Paragraph(new Run(c.Text)) { FontFamily = new FontFamily("Consolas, 微软雅黑"), Background = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, fontSize * 0.5) };
                    doc.Blocks.Add(p);
                    break;
                }
                case MdRule:
                    doc.Blocks.Add(new Paragraph { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, fontSize * 0.4, 0, fontSize * 0.6) });
                    break;
            }
        }
        return doc;
    }

    private static void AddSpans(Paragraph p, IReadOnlyList<MdSpan> spans, Brush mark)
    {
        foreach (var s in spans)
        {
            var lines = s.Text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) p.Inlines.Add(new LineBreak());
                var run = new Run(lines[i]);
                if (s.Bold) run.FontWeight = FontWeights.Bold;
                if (s.Italic) run.FontStyle = FontStyles.Italic;
                if (s.Mark) run.Background = mark;
                if (s.Code)
                {
                    run.FontFamily = new FontFamily("Consolas, 微软雅黑");
                    run.Background = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
                }
                p.Inlines.Add(run);
            }
        }
    }
}
