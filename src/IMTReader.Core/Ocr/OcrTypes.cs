using System.Windows;
using System.Windows.Media.Imaging;

namespace IMTReader.Core.Ocr;

public enum LineOrientation
{
    Unknown,
    Horizontal,
    Vertical
}

/// <summary>OCR 引擎输出的一行（横排）或一栏（竖排）文本，坐标为识别图像的像素坐标。</summary>
public sealed class OcrLine
{
    public string Text { get; set; } = string.Empty;
    public Point[] Quad { get; set; } = new Point[4];
    public double Score { get; set; } = 1;

    public Rect Bounds
    {
        get
        {
            if (Quad == null || Quad.Length == 0) return Rect.Empty;
            double minX = Quad.Min(p => p.X), maxX = Quad.Max(p => p.X);
            double minY = Quad.Min(p => p.Y), maxY = Quad.Max(p => p.Y);
            return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
        }
    }

    public LineOrientation Orientation
    {
        get
        {
            var b = Bounds;
            if (b.Height > b.Width * 1.8) return LineOrientation.Vertical;
            if (b.Width > b.Height * 1.8) return LineOrientation.Horizontal;
            return LineOrientation.Unknown;
        }
    }

    public static OcrLine FromRect(string text, Rect r, double score) => new()
    {
        Text = text,
        Score = score,
        Quad = new[] { r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft }
    };
}

public sealed class OcrPageResult
{
    public string EngineName { get; set; } = string.Empty;
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public List<OcrLine> Lines { get; } = new();
}

public sealed class OcrOptions
{
    /// <summary>拓片模式：先反白（白字黑底 → 黑字白底）再识别。</summary>
    public bool Rubbing { get; set; }

    /// <summary>识别前做二值化。</summary>
    public bool Binarize { get; set; }

    /// <summary>仅识别页面的某个区域（归一化坐标 0..1）。结果会追加到该页已有文本块之后。</summary>
    public Rect? Region { get; set; }

    public OcrOptions Clone() => (OcrOptions)MemberwiseClone();

    public string Describe()
    {
        var parts = new List<string>();
        if (Rubbing) parts.Add("拓片");
        if (Binarize) parts.Add("二值化");
        if (Region != null) parts.Add("框选");
        return parts.Count == 0 ? "常规" : string.Join("+", parts);
    }
}

public interface IOcrEngine
{
    string Name { get; }

    Task<OcrPageResult> RecognizeAsync(BitmapSource image, OcrOptions options, CancellationToken ct);
}
