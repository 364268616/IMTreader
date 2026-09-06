using IMTReader.Core.Ai;
using IMTReader.Core.Data;
using IMTReader.Core.Imaging;
using IMTReader.Core.Models;
using IMTReader.Core.Ocr;
using IMTReader.Core.Services;
using Xunit;

namespace IMTReader.Tests;

/// <summary>
/// 端到端流水线测试：页面源 → PaddleOCR → 版面分析 → 数据库。需要输出目录中包含 PaddleOCR 运行时与模型。
/// 测试图片为合成的竖排繁体《论语》页、白字黑底“拓片”页与横排简体页。
/// </summary>
public class OcrPipelineTests : IDisposable
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "TestData");
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "imtreader-it-" + Guid.NewGuid().ToString("N"));
    private readonly LibraryDatabase _db;
    private readonly SettingsStore _settings;
    private readonly OcrEngineFactory _engines;
    private readonly OcrService _ocr;

    public OcrPipelineTests()
    {
        Directory.CreateDirectory(_tempDir);
        _db = new LibraryDatabase(Path.Combine(_tempDir, "lib.db"));
        _settings = SettingsStore.Load(Path.Combine(_tempDir, "settings.json"));
        _settings.Update(s =>
        {
            s.OcrEngine = OcrEngineKind.Paddle;
            s.OcrThreads = 4;
            s.OcrMaxSideLen = 1600;
        });
        var log = new Logger(null);
        _engines = new OcrEngineFactory(_settings, new AiClient(() => _settings.Current, log));
        _ocr = new OcrService(_db, _engines, _settings, log);
    }

    private static string[] Pages => new[]
    {
        Path.Combine(DataDir, "page1_vertical.png"),
        Path.Combine(DataDir, "page2_rubbing.png"),
        Path.Combine(DataDir, "page3_horizontal.png")
    };

    private DocumentInfo AddImageDocument()
    {
        var doc = new DocumentInfo
        {
            Title = "合成测试",
            Kind = SourceKind.ImageFiles,
            SourcePath = string.Join(DocumentInfo.PathSeparator, Pages),
            PageCount = 3
        };
        _db.AddDocument(doc);
        return doc;
    }

    /// <summary>在专用线程上执行，确保与创建位图的线程不同（Task.Run 可能复用同一线程而漏掉线程亲和问题）。</summary>
    private static T OnOtherThread<T>(Func<T> func)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = func(); }
            catch (Exception ex) { error = ex; }
        });
        thread.Start();
        thread.Join();
        if (error != null) throw new Xunit.Sdk.XunitException("其它线程访问失败：" + error);
        return result;
    }

    [Fact]
    public void RenderedPagesSurviveHandoffBetweenThreads()
    {
        var doc = AddImageDocument();
        using var source = PageSourceFactory.Open(doc);

        // 渲染线程 → 识别线程：页面位图必须能在另一线程读取属性、像素，并再次参与冻结
        var image = OnOtherThread(() => source.RenderPage(0, 150));
        Assert.True(image.IsFrozen, "页面位图必须冻结");
        Assert.Equal(image.PixelWidth, OnOtherThread(() =>
        {
            using var mat = BitmapUtil.ToMat(image);
            _ = BitmapUtil.Resize(image, 0.5); // TransformedBitmap + Freeze，会重新遍历整条位图链
            return mat.Width;
        }));

        var thumb = OnOtherThread(() => source.RenderThumbnail(1, 140));
        Assert.True(thumb.IsFrozen);
        OnOtherThread(() => BitmapUtil.Resize(thumb, 0.5));

        using var pdfSource = new PdfPageSource(Path.Combine(DataDir, "sample.pdf"));
        var pdfImage = OnOtherThread(() => pdfSource.RenderPage(0, 100));
        Assert.True(pdfImage.IsFrozen);
        Assert.Equal(pdfImage.PixelWidth, OnOtherThread(() =>
        {
            using var mat = BitmapUtil.ToMat(pdfImage);
            _ = BitmapUtil.Rotate(pdfImage, 90);
            return mat.Width;
        }));
    }

    [Fact]
    public void PaddleRuntimeIsDeployed()
    {
        Assert.True(PaddleOcrEngine.IsRuntimeAvailable(), "输出目录缺少 paddle_inference_c.dll 或 OcrModels 模型目录");
        Assert.All(Pages, p => Assert.True(File.Exists(p), "缺少测试图片 " + p));
    }

    [Fact]
    public async Task VerticalTraditionalPageIsRecognizedInReadingOrder()
    {
        var doc = AddImageDocument();
        using var source = PageSourceFactory.Open(doc);
        Assert.Equal(3, source.PageCount);

        var blocks = await _ocr.RecognizePageAsync(doc, source, 0, new OcrOptions(), CancellationToken.None);

        Assert.True(blocks.Count >= 2, "应至少识别出标题块与正文块，实际 " + blocks.Count);
        Assert.All(blocks, b =>
        {
            Assert.InRange(b.X, 0, 1);
            Assert.InRange(b.Y, 0, 1);
            Assert.InRange(b.X + b.W, 0, 1.001);
            Assert.InRange(b.Y + b.H, 0, 1.001);
            Assert.True(b.IsVertical);
        });
        Assert.Contains("論語學而", blocks[0].Text);
        var body = blocks.First(b => b.Text.Contains("子曰學而時習之"));
        var lines = body.Text.Split('\n');
        Assert.StartsWith("子曰學而時習之", lines[0]);
        Assert.Contains("傳不習乎", lines[^1]);

        var stored = _db.GetPageBlocks(doc.Id, 0);
        Assert.Equal(blocks.Count, stored.Count);
        Assert.Equal(OcrStatus.Done, _db.GetPageStatuses(doc.Id)[0].Status);
    }

    [Fact]
    public async Task RubbingModeRecognizesWhiteOnBlack()
    {
        var doc = AddImageDocument();
        using var source = PageSourceFactory.Open(doc);
        var blocks = await _ocr.RecognizePageAsync(doc, source, 1, new OcrOptions { Rubbing = true }, CancellationToken.None);
        var all = string.Concat(blocks.Select(b => b.Text));
        Assert.Contains("學而時習之", all);
    }

    [Fact]
    public async Task HorizontalSimplifiedPageIsRecognized()
    {
        var doc = AddImageDocument();
        using var source = PageSourceFactory.Open(doc);
        var blocks = await _ocr.RecognizePageAsync(doc, source, 2, new OcrOptions(), CancellationToken.None);
        var all = string.Concat(blocks.Select(b => b.Text));
        Assert.Contains("中华人民共和国", all);
        Assert.All(blocks, b => Assert.False(b.IsVertical));
    }

    [Fact]
    public async Task RegionRecognitionAppendsBlocks()
    {
        var doc = AddImageDocument();
        using var source = PageSourceFactory.Open(doc);
        await _ocr.RecognizePageAsync(doc, source, 0, new OcrOptions(), CancellationToken.None);
        int before = _db.GetPageBlocks(doc.Id, 0).Count;
        // 右侧标题栏区域（归一化坐标）
        var region = new System.Windows.Rect(0.88, 0.03, 0.08, 0.36);
        var blocks = await _ocr.RecognizePageAsync(doc, source, 0, new OcrOptions { Region = region }, CancellationToken.None);
        Assert.True(blocks.Count > before);
        var appended = blocks[^1];
        Assert.Contains("論語", appended.Text);
        Assert.InRange(appended.X, 0.85, 1);
    }

    [Fact]
    public void PdfPagesRenderAtRequestedDpi()
    {
        var pdf = Path.Combine(DataDir, "sample.pdf");
        Assert.True(File.Exists(pdf));
        using var source = new PdfPageSource(pdf);
        Assert.Equal(3, source.PageCount);
        var page = source.RenderPage(0, 100);
        Assert.True(page.PixelWidth > 500 && page.PixelHeight > 400);
        Assert.True(page.IsFrozen);
        var thumb = source.RenderThumbnail(1, 140);
        Assert.InRange(thumb.PixelWidth, 100, 200);
    }

    [Fact]
    public async Task PdfPipelineRecognizesText()
    {
        var pdf = Path.Combine(DataDir, "sample.pdf");
        var doc = new DocumentInfo { Title = "PDF 测试", Kind = SourceKind.Pdf, SourcePath = pdf, PageCount = 3 };
        _db.AddDocument(doc);
        using var source = PageSourceFactory.Open(doc);
        var blocks = await _ocr.RecognizePageAsync(doc, source, 2, new OcrOptions(), CancellationToken.None);
        Assert.Contains("古籍阅读器", string.Concat(blocks.Select(b => b.Text)));
    }

    [Fact]
    public async Task WindowsOcrRecognizesHorizontalTextWhenLanguagePackInstalled()
    {
        if (WindowsOcrEngine.AvailableLanguages().Count == 0) return; // 未安装语言包时跳过
        var engine = new WindowsOcrEngine("auto");
        var image = BitmapUtil.LoadFrame(Pages[2]);
        var result = await engine.RecognizeAsync(image, new OcrOptions(), CancellationToken.None);
        var text = string.Concat(result.Lines.Select(l => l.Text));
        Assert.Contains("中华", text);
        Assert.DoesNotContain("中 华", text);
    }

    public void Dispose()
    {
        _engines.Dispose();
        _db.Dispose();
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
