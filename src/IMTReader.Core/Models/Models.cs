namespace IMTReader.Core.Models;

/// <summary>文献来源类型。</summary>
public enum SourceKind
{
    Pdf = 0,
    ImageFiles = 1,
    ImageFolder = 2
}

/// <summary>页面 OCR 状态。</summary>
public enum OcrStatus
{
    None = 0,
    Done = 1,
    Failed = 2
}

/// <summary>文献库中的一部文献（PDF、图片集或图片文件夹）。</summary>
public sealed class DocumentInfo
{
    public const char PathSeparator = '|';

    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public SourceKind Kind { get; set; }

    /// <summary>PDF 文件路径、图片文件夹路径，或以 '|' 分隔的图片文件列表。</summary>
    public string SourcePath { get; set; } = string.Empty;

    public int PageCount { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public DateTime? LastOpenedAt { get; set; }
    public int LastPage { get; set; }

    /// <summary>已完成 OCR 的页数（由数据库统计填充）。</summary>
    public int OcrDonePages { get; set; }

    public string[] GetImageFiles() =>
        Kind == SourceKind.ImageFiles
            ? SourcePath.Split(PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();

    public bool SourceExists() => Kind switch
    {
        SourceKind.Pdf => File.Exists(SourcePath),
        SourceKind.ImageFolder => Directory.Exists(SourcePath),
        _ => GetImageFiles().Length > 0 && GetImageFiles().All(File.Exists)
    };

    public string KindText => Kind switch
    {
        SourceKind.Pdf => "PDF",
        SourceKind.ImageFolder => "图片文件夹",
        _ => "图片"
    };

    public override string ToString() => Title;
}

public sealed class PageInfo
{
    public long DocumentId { get; set; }
    public int PageIndex { get; set; }
    public OcrStatus Status { get; set; }
    public DateTime? OcrAt { get; set; }
    public string? Engine { get; set; }
}

/// <summary>
/// 一个识别文本块。坐标为相对页面尺寸的归一化值（0..1），与渲染分辨率无关。
/// 竖排文本块中每一行对应原图的一竖栏，行之间以换行符分隔。
/// </summary>
public sealed class OcrBlock
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public int PageIndex { get; set; }
    public int Order { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public bool IsVertical { get; set; }
    public string Text { get; set; } = string.Empty;
    public double Score { get; set; }

    public OcrBlock Clone() => (OcrBlock)MemberwiseClone();
}

/// <summary>文本高亮（以文本块内字符偏移记录）。</summary>
public sealed class Highlight
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public int PageIndex { get; set; }
    public long BlockId { get; set; }
    public int Start { get; set; }
    public int Length { get; set; }
    public string Color { get; set; } = "#FFF176";
    public string Excerpt { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class Bookmark
{
    public long Id { get; set; }
    public long DocumentId { get; set; }
    public int PageIndex { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class SearchHit
{
    public long DocumentId { get; set; }
    public string DocumentTitle { get; set; } = string.Empty;
    public int PageIndex { get; set; }
    public long BlockId { get; set; }
    public int BlockOrder { get; set; }
    public int Count { get; set; }
    public string Snippet { get; set; } = string.Empty;

    /// <summary>显示用页码（从 1 开始）。</summary>
    public int PageNumber => PageIndex + 1;

    /// <summary>显示用块号（从 1 开始）。</summary>
    public int BlockNumber => BlockOrder + 1;
}

public sealed class SearchResult
{
    public string Keyword { get; set; } = string.Empty;
    public string NormalizedKeyword { get; set; } = string.Empty;
    public List<SearchHit> Hits { get; } = new();
    public int TotalMatches { get; set; }
    public int PageCount { get; set; }
    public string? Error { get; set; }
}
