using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using IMTReader.Core.Export;
using IMTReader.Core.Imaging;
using IMTReader.Core.Models;
using IMTReader.Core.Ocr;
using IMTReader.Core.Text;

namespace IMTReader.App.ViewModels;

public enum ImageEnhanceMode
{
    None,
    Invert,
    Binarize,
    Grayscale,
    HighContrast
}

/// <summary>一部已打开文献的阅读状态：翻页、缩放、识别文本块、高亮、识别任务等。</summary>
public sealed class DocumentViewModel : ObservableObject, IDisposable
{
    private readonly PageCache _cache = new(12);
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private readonly SemaphoreSlim _thumbGate = new(2, 2);
    private CancellationTokenSource? _renderCts;
    private int _currentPage;
    private double _zoom = 0.7;
    private int _rotation;
    private BitmapSource? _currentImage;
    private BitmapSource? _rawPageImage;
    private bool _showSimplified;
    private double _fontSize;
    private bool _isSelectMode;
    private bool _isDirty;
    private string _statusText = string.Empty;
    private string _ocrStatusText = string.Empty;
    private ImageEnhanceMode _enhanceMode;
    private BlockViewModel? _selectedBlock;
    private Dictionary<int, PageInfo> _pageStatuses = new();
    private HashSet<int> _bookmarkedPages = new();
    private bool _isBusy;
    private bool _showBoxes;
    private OcrEngineKind _engineKind;

    public DocumentInfo Document { get; }
    public IPageSource Source { get; }
    public int PageCount => Source.PageCount;
    public ObservableCollection<BlockViewModel> Blocks { get; } = new();
    public ObservableCollection<PageThumbViewModel> Thumbnails { get; } = new();

    public event Action<int, long?, string?>? JumpRequested;
    public event Action? PageImageChanged;
    public event Action<BlockViewModel?>? SelectedBlockChanged;

    public DocumentViewModel(DocumentInfo document)
    {
        Document = document;
        Source = PageSourceFactory.Open(document);
        _currentPage = Math.Clamp(document.LastPage, 0, Math.Max(0, PageCount - 1));
        var s = AppServices.Settings.Current;
        _fontSize = s.FontSize;
        _showBoxes = s.ShowBlockBoxes;
        _engineKind = s.OcrEngine;
        for (int i = 0; i < PageCount; i++) Thumbnails.Add(new PageThumbViewModel(i));
        ReloadPageStatuses();
        ReloadBookmarks();
        AppServices.Jobs.PageCompleted += OnPageCompleted;
        AppServices.Jobs.JobChanged += OnJobChanged;
    }

    // ------------------------------------------------------------------ 属性

    public string Title => Document.Title;

    /// <summary>文本内容版本号：文本或识别结果变化时递增，供检索索引判断是否需要重建。</summary>
    public int ContentVersion { get; private set; }

    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(PageNumber));
                OnPropertyChanged(nameof(PageLabel));
                OnPropertyChanged(nameof(CanGoPrev));
                OnPropertyChanged(nameof(CanGoNext));
            }
        }
    }

    public int PageNumber => _currentPage + 1;
    public string PageLabel => $"/ {PageCount}";
    public bool CanGoPrev => _currentPage > 0;
    public bool CanGoNext => _currentPage < PageCount - 1;

    public double Zoom
    {
        get => _zoom;
        set
        {
            if (SetProperty(ref _zoom, Math.Clamp(value, 0.05, 8)))
                OnPropertyChanged(nameof(ZoomText));
        }
    }

    public string ZoomText => $"{Math.Round(_zoom * 100)}%";

    public int Rotation
    {
        get => _rotation;
        set => SetProperty(ref _rotation, ((value % 360) + 360) % 360);
    }

    public BitmapSource? CurrentImage
    {
        get => _currentImage;
        private set
        {
            if (SetProperty(ref _currentImage, value)) PageImageChanged?.Invoke();
        }
    }

    public bool ShowSimplified
    {
        get => _showSimplified;
        set
        {
            if (SetProperty(ref _showSimplified, value))
            {
                foreach (var b in Blocks) b.ShowSimplified = value;
                OnPropertyChanged(nameof(EditHint));
                OnPropertyChanged(nameof(IsEditable));
            }
        }
    }

    public bool IsEditable => !_showSimplified;

    public string EditHint => _showSimplified
        ? "简体显示模式（只读）。切回原文后可编辑。"
        : "修改后点「保存修改」写回本页 OCR 结果";

    public double FontSize
    {
        get => _fontSize;
        set => SetProperty(ref _fontSize, Math.Clamp(value, 9, 48));
    }

    public bool IsSelectMode
    {
        get => _isSelectMode;
        set => SetProperty(ref _isSelectMode, value);
    }

    public bool ShowBoxes
    {
        get => _showBoxes;
        set => SetProperty(ref _showBoxes, value);
    }

    public OcrEngineKind EngineKind
    {
        get => _engineKind;
        set
        {
            if (SetProperty(ref _engineKind, value))
                AppServices.Settings.Update(s => s.OcrEngine = value);
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (SetProperty(ref _isDirty, value)) OnPropertyChanged(nameof(BlocksHeader));
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string OcrStatusText
    {
        get => _ocrStatusText;
        set => SetProperty(ref _ocrStatusText, value);
    }

    public ImageEnhanceMode EnhanceMode
    {
        get => _enhanceMode;
        set
        {
            if (SetProperty(ref _enhanceMode, value)) _ = ApplyEnhanceAsync();
        }
    }

    public BlockViewModel? SelectedBlock
    {
        get => _selectedBlock;
        set
        {
            if (_selectedBlock == value) return;
            if (_selectedBlock != null) _selectedBlock.IsSelected = false;
            _selectedBlock = value;
            if (_selectedBlock != null) _selectedBlock.IsSelected = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedBlockModel));
            SelectedBlockChanged?.Invoke(value);
        }
    }

    public OcrBlock? SelectedBlockModel => _selectedBlock?.Block;

    public string BlocksHeader => $"识别文本（{Blocks.Count} 块，可编辑）" + (_isDirty ? " ●未保存" : string.Empty);

    public bool CurrentPageHasOcr => _pageStatuses.TryGetValue(_currentPage, out var p) && p.Status == OcrStatus.Done;

    public bool IsCurrentPageBookmarked => _bookmarkedPages.Contains(_currentPage);

    public IReadOnlyList<OcrBlock> BlockModels => Blocks.Select(b => b.Block).ToList();

    // ------------------------------------------------------------------ 翻页与渲染

    public async Task GoToPageAsync(int pageIndex, long? selectBlockId = null)
    {
        pageIndex = Math.Clamp(pageIndex, 0, Math.Max(0, PageCount - 1));
        if (pageIndex != _currentPage || CurrentImage == null)
        {
            if (IsDirty) SaveChanges();
            CurrentPage = pageIndex;
            AppServices.Db.TouchDocument(Document.Id, pageIndex);
            Document.LastPage = pageIndex;
            foreach (var t in Thumbnails) t.IsCurrent = t.PageIndex == pageIndex;
            OnPropertyChanged(nameof(IsCurrentPageBookmarked));
            LoadBlocks();
            await RenderCurrentPageAsync();
        }
        if (selectBlockId != null)
            SelectedBlock = Blocks.FirstOrDefault(b => b.Id == selectBlockId.Value);
    }

    public Task NextPageAsync() => GoToPageAsync(_currentPage + 1);
    public Task PrevPageAsync() => GoToPageAsync(_currentPage - 1);

    private async Task RenderCurrentPageAsync()
    {
        _renderCts?.Cancel();
        var cts = _renderCts = new CancellationTokenSource();
        int page = _currentPage;
        int dpi = AppServices.Settings.Current.RenderDpi;
        IsBusy = true;
        StatusText = $"正在渲染第 {page + 1} 页…";
        try
        {
            await _renderGate.WaitAsync(cts.Token);
            try
            {
                BitmapSource img;
                if (!_cache.TryGet(page, dpi, out img))
                {
                    img = await Task.Run(() => Source.RenderPage(page, dpi), cts.Token);
                    _cache.Put(page, dpi, img);
                }
                if (cts.IsCancellationRequested) return;
                _rawPageImage = img;
                await ApplyEnhanceAsync(cts.Token);
                StatusText = $"渲染完成（{img.PixelWidth}×{img.PixelHeight}），{Blocks.Count} 文本块";
            }
            finally
            {
                _renderGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppServices.Log.Error($"渲染第 {page + 1} 页失败", ex);
            StatusText = "渲染失败：" + ex.Message;
        }
        finally
        {
            if (!cts.IsCancellationRequested) IsBusy = false;
        }
    }

    private async Task ApplyEnhanceAsync(CancellationToken ct = default)
    {
        var raw = _rawPageImage;
        if (raw == null) return;
        var mode = _enhanceMode;
        var result = mode == ImageEnhanceMode.None
            ? raw
            : await Task.Run(() => mode switch
            {
                ImageEnhanceMode.Invert => BitmapUtil.Invert(raw),
                ImageEnhanceMode.Binarize => BitmapUtil.Binarize(raw),
                ImageEnhanceMode.Grayscale => BitmapUtil.ToGrayscale(raw),
                ImageEnhanceMode.HighContrast => BitmapUtil.AdjustContrast(raw, 1.6, -10),
                _ => raw
            }, ct);
        if (ct.IsCancellationRequested || raw != _rawPageImage) return;
        CurrentImage = result;
    }

    /// <summary>获取当前页原始渲染图（未增强），用于导出/复制图片。</summary>
    public BitmapSource? RawPageImage => _rawPageImage;

    public async Task EnsureThumbnailAsync(PageThumbViewModel thumb)
    {
        if (thumb.Image != null || thumb.IsLoading) return;
        thumb.IsLoading = true;
        try
        {
            await _thumbGate.WaitAsync();
            try
            {
                var img = await Task.Run(() => Source.RenderThumbnail(thumb.PageIndex, 140));
                thumb.Image = img;
            }
            finally
            {
                _thumbGate.Release();
            }
        }
        catch (Exception ex)
        {
            AppServices.Log.Warn($"缩略图渲染失败（第 {thumb.PageIndex + 1} 页）：{ex.Message}");
        }
        finally
        {
            thumb.IsLoading = false;
        }
    }

    // ------------------------------------------------------------------ 文本块

    public void LoadBlocks()
    {
        var blocks = AppServices.Db.GetPageBlocks(Document.Id, _currentPage);
        var highlights = AppServices.Db.GetPageHighlights(Document.Id, _currentPage).ToLookup(h => h.BlockId);
        SelectedBlock = null;
        Blocks.Clear();
        foreach (var b in blocks)
            Blocks.Add(new BlockViewModel(b, highlights[b.Id]) { ShowSimplified = _showSimplified });
        IsDirty = false;
        OnPropertyChanged(nameof(BlocksHeader));
        OnPropertyChanged(nameof(CurrentPageHasOcr));
        OnPropertyChanged(nameof(BlockModels));
    }

    /// <summary>把编辑器中的文本与高亮写回数据库。</summary>
    public void SaveChanges()
    {
        int changed = 0;
        foreach (var vm in Blocks)
        {
            if (vm.Editor == null || _showSimplified) continue;
            var (text, highlights) = vm.Editor.Extract();
            bool textChanged = text != vm.Block.Text;
            if (textChanged)
            {
                AppServices.Db.UpdateBlockText(vm.Id, text);
                vm.SetText(text);
                changed++;
            }
            var hs = highlights.Select(h => new Highlight
            {
                DocumentId = Document.Id,
                PageIndex = _currentPage,
                BlockId = vm.Id,
                Start = h.Start,
                Length = h.Length,
                Color = h.Color,
                Excerpt = SafeSubstring(text, h.Start, h.Length)
            }).ToList();
            if (!HighlightsEqual(hs, vm.Highlights))
            {
                AppServices.Db.ReplaceBlockHighlights(Document.Id, _currentPage, vm.Id, hs);
                vm.SetHighlights(hs);
                changed++;
            }
        }
        IsDirty = false;
        OnPropertyChanged(nameof(BlockModels));
        StatusText = changed > 0 ? $"已保存修改（{changed} 处）" : "没有需要保存的修改";
        if (changed > 0)
        {
            ContentVersion++;
            AppServices.Log.Info($"保存修改：《{Document.Title}》第 {_currentPage + 1} 页，{changed} 处");
        }
    }

    private static bool HighlightsEqual(IReadOnlyList<Highlight> a, IReadOnlyList<Highlight> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i].Start != b[i].Start || a[i].Length != b[i].Length || !string.Equals(a[i].Color, b[i].Color, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    private static string SafeSubstring(string s, int start, int len)
    {
        if (string.IsNullOrEmpty(s) || start < 0 || start >= s.Length) return string.Empty;
        return s.Substring(start, Math.Clamp(len, 0, s.Length - start));
    }

    public void ClearPageHighlights()
    {
        foreach (var vm in Blocks) vm.Editor?.ClearHighlights();
        AppServices.Db.ClearPageHighlights(Document.Id, _currentPage);
        foreach (var vm in Blocks) vm.SetHighlights(Array.Empty<Highlight>());
        StatusText = "已清除本页全部高亮";
    }

    public void DeleteBlock(BlockViewModel vm)
    {
        AppServices.Db.DeleteBlock(vm.Id);
        ContentVersion++;
        Blocks.Remove(vm);
        if (SelectedBlock == vm) SelectedBlock = null;
        OnPropertyChanged(nameof(BlocksHeader));
        OnPropertyChanged(nameof(BlockModels));
    }

    public void DeletePageResults()
    {
        AppServices.Db.DeletePageBlocks(Document.Id, _currentPage);
        ContentVersion++;
        ReloadPageStatuses();
        LoadBlocks();
        StatusText = "已删除本页识别结果";
    }

    /// <summary>用新文本替换某块内容（AI 标点等），并标记未保存。</summary>
    public void ReplaceBlockText(BlockViewModel vm, string newText)
    {
        vm.Editor?.LoadText(newText, vm.Highlights);
        IsDirty = true;
    }

    // ------------------------------------------------------------------ OCR

    private OcrOptions MakeOptions(bool rubbing) => new()
    {
        Rubbing = rubbing,
        Binarize = rubbing && AppServices.Settings.Current.RubbingBinarize
    };

    public void RecognizeCurrentPage(bool rubbing)
    {
        var title = (rubbing ? "单页拓片识别" : "单页识别") + $"（第 {_currentPage + 1} 页）";
        AppServices.Jobs.Enqueue(Document, new[] { _currentPage }, MakeOptions(rubbing), title);
        OcrStatusText = title + "：已加入队列";
    }

    public void RecognizeAllPages(bool rubbing, bool skipDone)
    {
        var pages = Enumerable.Range(0, PageCount)
            .Where(p => !skipDone || !(_pageStatuses.TryGetValue(p, out var st) && st.Status == OcrStatus.Done))
            .ToArray();
        if (pages.Length == 0)
        {
            OcrStatusText = "全部页面已识别";
            return;
        }
        var title = (rubbing ? "全文拓片识别" : "全文识别") + $"（{pages.Length} 页）";
        AppServices.Jobs.Enqueue(Document, pages, MakeOptions(rubbing), title);
        OcrStatusText = title + "：已加入队列";
    }

    public void RecognizeRange(int fromPage, int toPage, bool rubbing)
    {
        fromPage = Math.Clamp(fromPage, 1, PageCount);
        toPage = Math.Clamp(toPage, fromPage, PageCount);
        var pages = Enumerable.Range(fromPage - 1, toPage - fromPage + 1).ToArray();
        var title = (rubbing ? "范围拓片识别" : "范围识别") + $"（{fromPage}–{toPage} 页）";
        AppServices.Jobs.Enqueue(Document, pages, MakeOptions(rubbing), title);
        OcrStatusText = title + "：已加入队列";
    }

    public void RecognizeRegion(Rect normalizedRegion, bool rubbing)
    {
        var opts = MakeOptions(rubbing || _enhanceMode == ImageEnhanceMode.Invert);
        opts.Region = normalizedRegion;
        AppServices.Jobs.Enqueue(Document, new[] { _currentPage }, opts, $"框选识别（第 {_currentPage + 1} 页）");
        OcrStatusText = "框选识别：已加入队列";
    }

    private void OnPageCompleted(OcrJob job, int page, IReadOnlyList<OcrBlock> blocks)
    {
        if (job.Document.Id != Document.Id) return;
        ContentVersion++;
        ReloadPageStatuses();
        var thumb = Thumbnails.ElementAtOrDefault(page);
        if (thumb != null) thumb.HasOcr = true;
        if (page == _currentPage)
        {
            LoadBlocks();
            StatusText = $"识别完成：{Blocks.Count} 文本块";
        }
    }

    private void OnJobChanged(OcrJob job)
    {
        if (job.Document.Id != Document.Id) return;
        OcrStatusText = $"{job.Title}：{job.StatusText}" + (job.Error != null && job.Status == OcrJobStatus.Failed ? "　" + job.Error : string.Empty);
    }

    private void ReloadPageStatuses()
    {
        _pageStatuses = AppServices.Db.GetPageStatuses(Document.Id);
        foreach (var t in Thumbnails)
            t.HasOcr = _pageStatuses.TryGetValue(t.PageIndex, out var p) && p.Status == OcrStatus.Done;
        Document.OcrDonePages = _pageStatuses.Values.Count(p => p.Status == OcrStatus.Done);
        OnPropertyChanged(nameof(CurrentPageHasOcr));
        OnPropertyChanged(nameof(OcrSummary));
    }

    public string OcrSummary => $"已识别 {Document.OcrDonePages}/{PageCount} 页";

    // ------------------------------------------------------------------ 书签

    public void ReloadBookmarks()
    {
        _bookmarkedPages = AppServices.Db.GetBookmarks(Document.Id).Select(b => b.PageIndex).ToHashSet();
        foreach (var t in Thumbnails) t.IsBookmarked = _bookmarkedPages.Contains(t.PageIndex);
        OnPropertyChanged(nameof(IsCurrentPageBookmarked));
    }

    public void ToggleBookmark(string? title = null)
    {
        var existing = AppServices.Db.GetBookmarks(Document.Id).FirstOrDefault(b => b.PageIndex == _currentPage);
        if (existing != null)
        {
            AppServices.Db.DeleteBookmark(existing.Id);
            StatusText = $"已删除第 {_currentPage + 1} 页书签";
        }
        else
        {
            AppServices.Db.AddBookmark(new Bookmark
            {
                DocumentId = Document.Id,
                PageIndex = _currentPage,
                Title = string.IsNullOrWhiteSpace(title) ? $"第 {_currentPage + 1} 页" : title
            });
            StatusText = $"已添加第 {_currentPage + 1} 页书签";
        }
        ReloadBookmarks();
    }

    public IReadOnlyList<Bookmark> GetBookmarks() => AppServices.Db.GetBookmarks(Document.Id);

    // ------------------------------------------------------------------ 导航/检索

    /// <summary>请求跳转到指定页与文本块（检索、高亮列表等调用）。</summary>
    public void RequestJump(int pageIndex, long? blockId, string? keyword) => JumpRequested?.Invoke(pageIndex, blockId, keyword);

    public string BuildFullText(bool markdown, bool pageMarkers, bool simplified, bool joinLines)
    {
        if (IsDirty) SaveChanges();
        var blocks = AppServices.Db.GetDocumentBlocks(Document.Id);
        return Exporter.BuildFullText(Document, blocks, markdown, pageMarkers, simplified, joinLines);
    }

    public string CurrentPageText(bool simplified)
    {
        var text = string.Join("\n\n", Blocks.Select(b => b.Editor?.Extract().Text ?? b.Text));
        return simplified ? ChineseConverter.ToSimplified(text) : text;
    }

    public string Citation(int? blockOrder = null) => Exporter.BuildCitation(Document, _currentPage, blockOrder);

    public void Dispose()
    {
        AppServices.Jobs.PageCompleted -= OnPageCompleted;
        AppServices.Jobs.JobChanged -= OnJobChanged;
        _renderCts?.Cancel();
        _cache.Clear();
        Source.Dispose();
    }
}
