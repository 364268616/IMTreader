using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace IMTReader.App.Controls;

/// <summary>
/// 竖排文本面板：文字自上而下、行（栏）自右向左排列，模拟古籍版式。
/// 每个换行开始新的一栏；空行产生一栏空白（段落间隔）。放在横向滚动的 ScrollViewer 中使用。
/// </summary>
public sealed class VerticalTextPanel : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(VerticalTextPanel), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
        nameof(FontSize), typeof(double), typeof(VerticalTextPanel), new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = DependencyProperty.Register(
        nameof(FontFamily), typeof(FontFamily), typeof(VerticalTextPanel), new FrameworkPropertyMetadata(new FontFamily("楷体"), FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(VerticalTextPanel), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ColumnSpacingProperty = DependencyProperty.Register(
        nameof(ColumnSpacing), typeof(double), typeof(VerticalTextPanel), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowColumnLinesProperty = DependencyProperty.Register(
        nameof(ShowColumnLines), typeof(bool), typeof(VerticalTextPanel), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CharsPerColumnProperty = DependencyProperty.Register(
        nameof(CharsPerColumn), typeof(int), typeof(VerticalTextPanel), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <summary>栏间距（以字号为单位）。</summary>
    public double ColumnSpacing { get => (double)GetValue(ColumnSpacingProperty); set => SetValue(ColumnSpacingProperty, value); }

    public bool ShowColumnLines { get => (bool)GetValue(ShowColumnLinesProperty); set => SetValue(ShowColumnLinesProperty, value); }

    /// <summary>每栏字数；0 表示按可用高度自动计算。</summary>
    public int CharsPerColumn { get => (int)GetValue(CharsPerColumnProperty); set => SetValue(CharsPerColumnProperty, value); }

    private readonly List<string> _columns = new();
    private double _cell;
    private double _colAdvance;
    private int _rows;
    private const double Padding = 24;

    private void Layout(double availableHeight)
    {
        _columns.Clear();
        _cell = FontSize * 1.25;
        _colAdvance = FontSize * (1 + Math.Max(0.2, ColumnSpacing));
        int rows = CharsPerColumn > 0
            ? CharsPerColumn
            : double.IsInfinity(availableHeight) || availableHeight <= 0
                ? 24
                : Math.Max(4, (int)((availableHeight - Padding * 2) / _cell));
        _rows = rows;

        var text = (Text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var para in text.Split('\n'))
        {
            var chars = TextElementsOf(para);
            if (chars.Count == 0)
            {
                _columns.Add(string.Empty);
                continue;
            }
            for (int i = 0; i < chars.Count; i += rows)
                _columns.Add(string.Concat(chars.Skip(i).Take(rows)));
        }
    }

    private static List<string> TextElementsOf(string s)
    {
        var list = new List<string>();
        var e = StringInfo.GetTextElementEnumerator(s);
        while (e.MoveNext()) list.Add(e.GetTextElement());
        return list;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Layout(availableSize.Height);
        double width = _columns.Count * _colAdvance + Padding * 2;
        double height = _rows * _cell + Padding * 2;
        return new Size(width, double.IsInfinity(availableSize.Height) ? height : Math.Max(height, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_columns.Count == 0) return;
        var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double right = ActualWidth - Padding;
        var linePen = new Pen(new SolidColorBrush(Color.FromArgb(60, 120, 90, 60)), 0.8);
        linePen.Freeze();

        for (int c = 0; c < _columns.Count; c++)
        {
            double x = right - (c + 1) * _colAdvance;
            if (ShowColumnLines)
                dc.DrawLine(linePen, new Point(x + _colAdvance - FontSize * 0.1, Padding), new Point(x + _colAdvance - FontSize * 0.1, Padding + _rows * _cell));
            var col = _columns[c];
            var chars = TextElementsOf(col);
            for (int r = 0; r < chars.Count; r++)
            {
                var ch = chars[r];
                var ft = new FormattedText(ch, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, FontSize, Foreground, dpi);
                double cx = x + (FontSize - ft.Width) / 2;
                double cy = Padding + r * _cell + (_cell - ft.Height) / 2;
                if (IsRotatedPunctuation(ch))
                {
                    dc.PushTransform(new RotateTransform(90, x + FontSize / 2, Padding + r * _cell + _cell / 2));
                    dc.DrawText(ft, new Point(cx, cy));
                    dc.Pop();
                }
                else dc.DrawText(ft, new Point(cx, cy));
            }
        }
    }

    private static bool IsRotatedPunctuation(string ch) =>
        ch is "（" or "）" or "「" or "」" or "『" or "』" or "《" or "》" or "〈" or "〉" or "【" or "】" or "—" or "…" or "-" or "(" or ")";
}
