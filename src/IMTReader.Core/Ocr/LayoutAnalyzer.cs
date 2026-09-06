using System.Windows;
using IMTReader.Core.Models;

namespace IMTReader.Core.Ocr;

/// <summary>
/// 版面分析：把引擎输出的行/栏按阅读顺序排列并合并为文本块。
/// 竖排：先按纵向空隙切分为“栏带”，带内各栏从右到左、栏内从上到下；相邻且间距小的栏合并为一块（段落）。
/// 横排：行从上到下、行内从左到右；相邻行合并为段落。
/// 输出的块坐标为像素坐标，由调用方归一化。
/// </summary>
public static class LayoutAnalyzer
{
    public sealed class Options
    {
        public bool MergeLines { get; set; } = true;
        public double MinScore { get; set; } = 0.3;
    }

    private readonly record struct Keyed(double K1, double K2, double K3, OcrBlock Block);

    public static List<OcrBlock> BuildBlocks(OcrPageResult result, Options? options = null)
    {
        options ??= new Options();
        var lines = result.Lines
            .Where(l => !string.IsNullOrWhiteSpace(l.Text) && l.Score >= options.MinScore)
            .Where(l => l.Bounds.Width > 0 && l.Bounds.Height > 0)
            .ToList();
        if (lines.Count == 0) return new List<OcrBlock>();

        int vChars = lines.Where(l => l.Orientation == LineOrientation.Vertical).Sum(l => l.Text.Length);
        int hChars = lines.Where(l => l.Orientation == LineOrientation.Horizontal).Sum(l => l.Text.Length);
        bool pageVertical = vChars >= hChars && vChars > 0;

        var vertical = new List<OcrLine>();
        var horizontal = new List<OcrLine>();
        foreach (var l in lines)
        {
            var o = l.Orientation;
            if (o == LineOrientation.Vertical || (o == LineOrientation.Unknown && pageVertical)) vertical.Add(l);
            else horizontal.Add(l);
        }

        var keyed = new List<Keyed>();
        keyed.AddRange(ArrangeVertical(vertical, options.MergeLines));
        keyed.AddRange(ArrangeHorizontal(horizontal, options.MergeLines));

        var ordered = keyed.OrderBy(k => k.K1).ThenBy(k => k.K2).ThenBy(k => k.K3).Select(k => k.Block).ToList();
        for (int i = 0; i < ordered.Count; i++) ordered[i].Order = i;
        return ordered;
    }

    // ------------------------------------------------------------------ 竖排

    private static List<Keyed> ArrangeVertical(List<OcrLine> lines, bool merge)
    {
        var result = new List<Keyed>();
        if (lines.Count == 0) return result;

        double charSize = Math.Max(1, Median(lines.Select(l => l.Bounds.Width)));
        var bands = SplitBands(lines.Select(l => (l.Bounds.Top, l.Bounds.Bottom)), charSize * 1.2);
        var byBand = bands.Select(_ => new List<OcrLine>()).ToList();
        foreach (var l in lines)
        {
            double cy = l.Bounds.Top + l.Bounds.Height / 2;
            int idx = 0;
            double best = double.MaxValue;
            for (int i = 0; i < bands.Count; i++)
            {
                var (t, b) = bands[i];
                double d = cy < t ? t - cy : cy > b ? cy - b : 0;
                if (d < best)
                {
                    best = d;
                    idx = i;
                }
            }
            byBand[idx].Add(l);
        }

        for (int bi = 0; bi < bands.Count; bi++)
        {
            var (bandTop, bandBottom) = bands[bi];
            var columns = ClusterByOverlap(byBand[bi], l => (l.Bounds.Left, l.Bounds.Right), descending: true);
            var units = new List<(OcrLine Line, int Col)>();
            for (int c = 0; c < columns.Count; c++)
                foreach (var l in columns[c].OrderBy(l => l.Bounds.Top))
                    units.Add((l, c));

            var group = new List<(OcrLine Line, int Unit)>();
            int groupCol = -1;

            void Flush()
            {
                if (group.Count == 0) return;
                var blk = MakeBlock(group, true);
                result.Add(new Keyed(bandTop, -(blk.X + blk.W), blk.Y, blk));
                group = new List<(OcrLine, int)>();
            }

            foreach (var (l, col) in units)
            {
                if (group.Count > 0)
                {
                    bool brk = !merge;
                    if (!brk)
                    {
                        var prev = group[^1].Line.Bounds;
                        var cur = l.Bounds;
                        if (col == groupCol)
                        {
                            brk = cur.Top - prev.Bottom > charSize * 1.5;
                        }
                        else
                        {
                            double gap = prev.Left - cur.Right;
                            double overlap = Math.Min(prev.Bottom, cur.Bottom) - Math.Max(prev.Top, cur.Top);
                            double widthRatio = Math.Max(prev.Width, cur.Width) / Math.Max(1, Math.Min(prev.Width, cur.Width));
                            bool prevShort = prev.Bottom < bandBottom - charSize * 1.5;
                            bool nextFull = cur.Top < bandTop + charSize * 1.5 && cur.Bottom > prev.Bottom + charSize;
                            brk = gap > charSize * 1.2
                                  || overlap < Math.Min(prev.Height, cur.Height) * 0.3
                                  || widthRatio > 1.6
                                  || (prevShort && nextFull);
                        }
                    }
                    if (brk) Flush();
                }
                group.Add((l, col));
                groupCol = col;
            }
            Flush();
        }
        return result;
    }

    // ------------------------------------------------------------------ 横排

    private static List<Keyed> ArrangeHorizontal(List<OcrLine> lines, bool merge)
    {
        var result = new List<Keyed>();
        if (lines.Count == 0) return result;

        double lineH = Math.Max(1, Median(lines.Select(l => l.Bounds.Height)));
        var rows = ClusterByOverlap(lines, l => (l.Bounds.Top, l.Bounds.Bottom), descending: false);
        var units = new List<(OcrLine Line, int Row)>();
        for (int r = 0; r < rows.Count; r++)
            foreach (var l in rows[r].OrderBy(l => l.Bounds.Left))
                units.Add((l, r));

        var group = new List<(OcrLine Line, int Unit)>();
        int groupRow = -1;
        Rect groupBounds = Rect.Empty;

        void Flush()
        {
            if (group.Count == 0) return;
            var blk = MakeBlock(group, false);
            result.Add(new Keyed(blk.Y, blk.X, 0, blk));
            group = new List<(OcrLine, int)>();
            groupBounds = Rect.Empty;
        }

        foreach (var (l, row) in units)
        {
            if (group.Count > 0)
            {
                bool brk = !merge;
                if (!brk)
                {
                    var prev = group[^1].Line.Bounds;
                    var cur = l.Bounds;
                    if (row == groupRow)
                    {
                        brk = cur.Left - prev.Right > lineH * 1.5;
                    }
                    else
                    {
                        double vgap = cur.Top - groupBounds.Bottom;
                        double xOverlap = Math.Min(groupBounds.Right, cur.Right) - Math.Max(groupBounds.Left, cur.Left);
                        double heightRatio = Math.Max(prev.Height, cur.Height) / Math.Max(1, Math.Min(prev.Height, cur.Height));
                        brk = vgap > lineH * 1.2
                              || xOverlap < Math.Min(groupBounds.Width, cur.Width) * 0.3
                              || heightRatio > 1.6;
                    }
                }
                if (brk) Flush();
            }
            group.Add((l, row));
            groupRow = row;
            groupBounds = groupBounds.IsEmpty ? l.Bounds : Rect.Union(groupBounds, l.Bounds);
        }
        Flush();
        return result;
    }

    // ------------------------------------------------------------------ 公共工具

    private static OcrBlock MakeBlock(List<(OcrLine Line, int Unit)> items, bool vertical)
    {
        var sb = new System.Text.StringBuilder();
        Rect bounds = Rect.Empty;
        double score = 0;
        int lastUnit = int.MinValue;
        foreach (var (line, unit) in items)
        {
            if (sb.Length > 0 && unit != lastUnit) sb.Append('\n');
            sb.Append(line.Text.Trim());
            bounds = bounds.IsEmpty ? line.Bounds : Rect.Union(bounds, line.Bounds);
            score += line.Score;
            lastUnit = unit;
        }
        return new OcrBlock
        {
            X = bounds.X,
            Y = bounds.Y,
            W = bounds.Width,
            H = bounds.Height,
            IsVertical = vertical,
            Text = sb.ToString(),
            Score = items.Count == 0 ? 0 : score / items.Count
        };
    }

    /// <summary>把区间按“空隙 ≥ minGap”切分为若干带。</summary>
    internal static List<(double Top, double Bottom)> SplitBands(IEnumerable<(double Top, double Bottom)> intervals, double minGap)
    {
        var bands = new List<(double Top, double Bottom)>();
        foreach (var (t, b) in intervals.OrderBy(i => i.Top))
        {
            if (bands.Count > 0 && t <= bands[^1].Bottom + minGap)
                bands[^1] = (bands[^1].Top, Math.Max(bands[^1].Bottom, b));
            else
                bands.Add((t, b));
        }
        return bands;
    }

    /// <summary>按区间重叠（≥ 较短者的一半）聚类；聚类顺序按中心位置升序或降序。</summary>
    internal static List<List<OcrLine>> ClusterByOverlap(List<OcrLine> lines, Func<OcrLine, (double A, double B)> range, bool descending)
    {
        var sorted = lines.OrderBy(l =>
        {
            var (a, b) = range(l);
            return (a + b) / 2;
        }).ToList();
        if (descending) sorted.Reverse();

        var clusters = new List<(double A, double B, List<OcrLine> Items)>();
        foreach (var l in sorted)
        {
            var (a, b) = range(l);
            int found = -1;
            for (int i = 0; i < clusters.Count; i++)
            {
                var c = clusters[i];
                double ov = Math.Min(c.B, b) - Math.Max(c.A, a);
                double minLen = Math.Min(c.B - c.A, b - a);
                if (ov >= minLen * 0.5)
                {
                    found = i;
                    break;
                }
            }
            if (found < 0)
                clusters.Add((a, b, new List<OcrLine> { l }));
            else
            {
                var c = clusters[found];
                c.Items.Add(l);
                clusters[found] = (Math.Min(c.A, a), Math.Max(c.B, b), c.Items);
            }
        }
        return clusters.Select(c => c.Items).ToList();
    }

    internal static double Median(IEnumerable<double> values)
    {
        var arr = values.OrderBy(v => v).ToArray();
        if (arr.Length == 0) return 0;
        return arr.Length % 2 == 1 ? arr[arr.Length / 2] : (arr[arr.Length / 2 - 1] + arr[arr.Length / 2]) / 2;
    }
}
