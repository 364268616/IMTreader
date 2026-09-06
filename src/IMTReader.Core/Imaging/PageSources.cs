using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using IMTReader.Core.Models;
using PDFtoImage;

namespace IMTReader.Core.Imaging;

/// <summary>页面图像来源（PDF 或图片集合）。实现须线程安全。</summary>
public interface IPageSource : IDisposable
{
    int PageCount { get; }

    /// <summary>渲染整页。对 PDF 使用给定 DPI；对图片忽略 DPI，返回原始分辨率。</summary>
    BitmapSource RenderPage(int index, int dpi);

    BitmapSource RenderThumbnail(int index, int maxWidth);
}

public sealed class PdfPageSource : IPageSource
{
    private readonly object _gate = new();
    private readonly FileStream _stream;
    private bool _disposed;

    public int PageCount { get; }

    public PdfPageSource(string path)
    {
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        lock (_gate)
        {
            PageCount = Conversion.GetPageCount(_stream, true, null);
        }
    }

    public BitmapSource RenderPage(int index, int dpi)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _stream.Position = 0;
            using var ms = new MemoryStream();
            Conversion.SavePng(ms, _stream, index, true, null, new RenderOptions { Dpi = Math.Clamp(dpi, 24, 600) });
            ms.Position = 0;
            return BitmapUtil.DecodeFrozen(ms);
        }
    }

    public BitmapSource RenderThumbnail(int index, int maxWidth)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _stream.Position = 0;
            var size = Conversion.GetPageSize(_stream, index, true, null); // 单位：点（1/72 英寸）
            _stream.Position = 0;
            int dpi = Math.Clamp((int)Math.Ceiling(maxWidth / Math.Max(1.0, size.Width / 72.0)), 8, 96);
            using var ms = new MemoryStream();
            Conversion.SavePng(ms, _stream, index, true, null, new RenderOptions { Dpi = dpi });
            ms.Position = 0;
            return BitmapUtil.DecodeFrozen(ms);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PdfPageSource));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _stream.Dispose();
        }
    }
}

public sealed class ImagePageSource : IPageSource
{
    private readonly List<(string File, int Frame)> _pages = new();

    public int PageCount => _pages.Count;

    public ImagePageSource(IEnumerable<string> files)
    {
        foreach (var f in files)
        {
            int frames = IsTiff(f) ? BitmapUtil.GetFrameCount(f) : 1;
            for (int i = 0; i < frames; i++) _pages.Add((f, i));
        }
    }

    public string GetPageFile(int index) => _pages[index].File;

    private static bool IsTiff(string f)
    {
        var ext = Path.GetExtension(f);
        return ext.Equals(".tif", StringComparison.OrdinalIgnoreCase) || ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
    }

    public BitmapSource RenderPage(int index, int dpi)
    {
        var (file, frame) = _pages[index];
        return BitmapUtil.LoadFrame(file, frame);
    }

    public BitmapSource RenderThumbnail(int index, int maxWidth)
    {
        var (file, frame) = _pages[index];
        return BitmapUtil.LoadThumbnail(file, frame, maxWidth);
    }

    /// <summary>列出文件夹中的图片文件，按 Windows 资源管理器的自然顺序排序。</summary>
    public static string[] ListFolderImages(string folder)
    {
        var files = Directory.EnumerateFiles(folder)
            .Where(BitmapUtil.IsSupportedImage)
            .ToArray();
        Array.Sort(files, NaturalComparer.Instance);
        return files;
    }

    public void Dispose()
    {
    }
}

/// <summary>自然排序（"页2" 排在 "页10" 之前），使用 shlwapi 的 StrCmpLogicalW。</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static NaturalComparer Instance { get; } = new();

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string x, string y);

    public int Compare(string? x, string? y)
    {
        if (x == null) return y == null ? 0 : -1;
        if (y == null) return 1;
        try { return StrCmpLogicalW(x, y); }
        catch { return string.Compare(x, y, StringComparison.OrdinalIgnoreCase); }
    }
}

public static class PageSourceFactory
{
    public static IPageSource Open(DocumentInfo doc) => doc.Kind switch
    {
        SourceKind.Pdf => new PdfPageSource(doc.SourcePath),
        SourceKind.ImageFolder => new ImagePageSource(ImagePageSource.ListFolderImages(doc.SourcePath)),
        _ => new ImagePageSource(doc.GetImageFiles())
    };

    /// <summary>统计来源页数（导入时使用）。</summary>
    public static int CountPages(SourceKind kind, string sourcePath)
    {
        using var src = Open(new DocumentInfo { Kind = kind, SourcePath = sourcePath });
        return src.PageCount;
    }
}
