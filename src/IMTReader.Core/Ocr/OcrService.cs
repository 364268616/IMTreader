using System.Windows;
using IMTReader.Core.Data;
using IMTReader.Core.Imaging;
using IMTReader.Core.Models;
using IMTReader.Core.Services;

namespace IMTReader.Core.Ocr;

/// <summary>OCR 流程编排：渲染页面 → 预处理 → 引擎识别 → 版面分析 → 归一化坐标 → 写入数据库。</summary>
public sealed class OcrService
{
    private readonly LibraryDatabase _db;
    private readonly OcrEngineFactory _engines;
    private readonly SettingsStore _settings;
    private readonly Logger _log;

    public OcrService(LibraryDatabase db, OcrEngineFactory engines, SettingsStore settings, Logger log)
    {
        _db = db;
        _engines = engines;
        _settings = settings;
        _log = log;
    }

    public async Task<List<OcrBlock>> RecognizePageAsync(DocumentInfo doc, IPageSource source, int pageIndex, OcrOptions options, CancellationToken ct)
    {
        var settings = _settings.Current;
        var engine = _engines.Get();

        var image = await Task.Run(() => source.RenderPage(pageIndex, settings.OcrDpi), ct);
        int fullW = image.PixelWidth, fullH = image.PixelHeight;
        double offX = 0, offY = 0;

        if (options.Region is { } region)
        {
            var px = new Int32Rect(
                (int)Math.Round(region.X * fullW), (int)Math.Round(region.Y * fullH),
                (int)Math.Round(region.Width * fullW), (int)Math.Round(region.Height * fullH));
            px = BitmapUtil.Clamp(px, fullW, fullH);
            image = BitmapUtil.Crop(image, px);
            offX = px.X;
            offY = px.Y;
        }

        if (options.Rubbing) image = await Task.Run(() => BitmapUtil.Invert(image), ct);
        if (options.Binarize) image = await Task.Run(() => BitmapUtil.Binarize(image), ct);

        var result = await engine.RecognizeAsync(image, options, ct);
        var blocks = LayoutAnalyzer.BuildBlocks(result, new LayoutAnalyzer.Options
        {
            MergeLines = settings.MergeLinesIntoBlocks,
            MinScore = settings.OcrMinScore
        });

        foreach (var b in blocks)
        {
            b.X = (b.X + offX) / fullW;
            b.Y = (b.Y + offY) / fullH;
            b.W = b.W / fullW;
            b.H = b.H / fullH;
            b.DocumentId = doc.Id;
            b.PageIndex = pageIndex;
        }

        if (options.Region != null) _db.AppendPageBlocks(doc.Id, pageIndex, blocks);
        else _db.ReplacePageBlocks(doc.Id, pageIndex, blocks);
        _db.SetPageStatus(doc.Id, pageIndex, OcrStatus.Done, engine.Name);

        _log.Info($"识别完成：《{doc.Title}》第 {pageIndex + 1} 页，{blocks.Count} 块（{engine.Name}，{options.Describe()}）");
        return _db.GetPageBlocks(doc.Id, pageIndex);
    }
}
