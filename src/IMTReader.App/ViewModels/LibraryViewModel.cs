using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using IMTReader.Core.Imaging;
using IMTReader.Core.Models;

namespace IMTReader.App.ViewModels;

public sealed class DocumentItemViewModel : ObservableObject
{
    public DocumentInfo Document { get; private set; }

    public DocumentItemViewModel(DocumentInfo doc)
    {
        Document = doc;
    }

    public long Id => Document.Id;
    public string Title => Document.Title;
    public string Kind => Document.KindText;
    public int PageCount => Document.PageCount;
    public string OcrProgress => $"{Document.OcrDonePages}/{Document.PageCount}";
    public string AddedAt => Document.AddedAt.ToString("yyyy-MM-dd HH:mm");
    public string LastOpened => Document.LastOpenedAt?.ToString("yyyy-MM-dd HH:mm") ?? "—";
    public string SourcePath => Document.Kind == SourceKind.ImageFiles ? $"{Document.GetImageFiles().Length} 个图片文件" : Document.SourcePath;
    public bool SourceMissing => !Document.SourceExists();
    public string LastPageText => $"第 {Document.LastPage + 1} 页";

    public void Update(DocumentInfo doc)
    {
        Document = doc;
        OnPropertyChanged(string.Empty);
    }
}

public sealed class LibraryViewModel : ObservableObject
{
    private string _filter = string.Empty;

    public ObservableCollection<DocumentItemViewModel> Documents { get; } = new();
    public ObservableCollection<DocumentItemViewModel> Recent { get; } = new();

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value)) Reload();
        }
    }

    /// <summary>文献库是否为空，用于显示空状态提示。</summary>
    public bool IsEmpty => Documents.Count == 0;

    public void Reload()
    {
        Documents.Clear();
        foreach (var d in AppServices.Db.GetDocuments())
        {
            if (_filter.Length > 0 && !d.Title.Contains(_filter, StringComparison.OrdinalIgnoreCase)) continue;
            Documents.Add(new DocumentItemViewModel(d));
        }
        Recent.Clear();
        foreach (var d in AppServices.Db.GetRecentDocuments(20)) Recent.Add(new DocumentItemViewModel(d));
        OnPropertyChanged(nameof(IsEmpty));
    }

    public DocumentInfo ImportPdf(string path)
    {
        var existing = AppServices.Db.FindDocumentBySource(path);
        if (existing != null) return existing;
        var doc = new DocumentInfo
        {
            Title = Path.GetFileNameWithoutExtension(path),
            Kind = SourceKind.Pdf,
            SourcePath = path,
            PageCount = PageSourceFactory.CountPages(SourceKind.Pdf, path)
        };
        AppServices.Db.AddDocument(doc);
        AppServices.Log.Info($"导入 PDF：{path}（{doc.PageCount} 页）");
        Reload();
        return doc;
    }

    public DocumentInfo ImportImages(IReadOnlyList<string> files, string? title = null)
    {
        var sorted = files.ToArray();
        Array.Sort(sorted, NaturalComparer.Instance);
        var source = string.Join(DocumentInfo.PathSeparator, sorted);
        var existing = AppServices.Db.FindDocumentBySource(source);
        if (existing != null) return existing;
        var doc = new DocumentInfo
        {
            Title = title ?? (sorted.Length == 1 ? Path.GetFileNameWithoutExtension(sorted[0]) : Path.GetFileName(Path.GetDirectoryName(sorted[0]) ?? "图片集") + $"（{sorted.Length} 张）"),
            Kind = SourceKind.ImageFiles,
            SourcePath = source,
            PageCount = PageSourceFactory.CountPages(SourceKind.ImageFiles, source)
        };
        AppServices.Db.AddDocument(doc);
        AppServices.Log.Info($"导入图片：{sorted.Length} 个文件（{doc.PageCount} 页）");
        Reload();
        return doc;
    }

    public DocumentInfo ImportFolder(string folder)
    {
        var existing = AppServices.Db.FindDocumentBySource(folder);
        if (existing != null) return existing;
        int pages = PageSourceFactory.CountPages(SourceKind.ImageFolder, folder);
        if (pages == 0) throw new InvalidOperationException("该文件夹中没有可识别的图片文件。");
        var doc = new DocumentInfo
        {
            Title = Path.GetFileName(folder.TrimEnd('\\', '/')),
            Kind = SourceKind.ImageFolder,
            SourcePath = folder,
            PageCount = pages
        };
        AppServices.Db.AddDocument(doc);
        AppServices.Log.Info($"导入文件夹：{folder}（{pages} 页）");
        Reload();
        return doc;
    }

    public void Delete(DocumentItemViewModel item)
    {
        AppServices.Db.DeleteDocument(item.Id);
        AppServices.Log.Info($"从文献库移除：《{item.Title}》");
        Reload();
    }

    public void Rename(DocumentItemViewModel item, string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        AppServices.Db.RenameDocument(item.Id, title.Trim());
        Reload();
    }

    public void Relocate(DocumentItemViewModel item, string newPath)
    {
        AppServices.Db.UpdateSourcePath(item.Id, newPath);
        Reload();
    }
}
