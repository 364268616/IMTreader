using System.Windows;
using System.Windows.Media.Imaging;
using IMTReader.Core.Ai;
using IMTReader.Core.Imaging;

namespace IMTReader.Core.Ocr;

/// <summary>
/// 通过视觉大模型（OpenAI 兼容接口）识别页面。适合双行小注、残损、混排等传统 OCR 难以处理的版面。
/// 大模型不返回精确坐标，因此每个段落用等分的伪坐标框表示，仅用于定位段落大致位置。
/// </summary>
public sealed class VisionLlmOcrEngine : IOcrEngine
{
    private readonly AiClient _ai;

    public VisionLlmOcrEngine(AiClient ai)
    {
        _ai = ai;
    }

    public string Name => "AI 视觉模型";

    public async Task<OcrPageResult> RecognizeAsync(BitmapSource image, OcrOptions options, CancellationToken ct)
    {
        if (!_ai.IsConfigured)
            throw new InvalidOperationException("未配置 AI 服务，请在“设置”中填写接口地址、模型与密钥。");

        var img = image;
        int longest = Math.Max(image.PixelWidth, image.PixelHeight);
        if (longest > 2200) img = BitmapUtil.Resize(image, 2200.0 / longest);
        var png = BitmapUtil.EncodePng(img);

        var text = await _ai.VisionOcrAsync(png, ct);
        var page = new OcrPageResult
        {
            EngineName = Name,
            ImageWidth = image.PixelWidth,
            ImageHeight = image.PixelHeight
        };

        var paragraphs = SplitParagraphs(text);
        if (paragraphs.Count == 0) return page;
        double rowH = image.PixelHeight / (double)paragraphs.Count;
        for (int i = 0; i < paragraphs.Count; i++)
        {
            var rect = new Rect(0, i * rowH, image.PixelWidth, Math.Max(1, rowH - 1));
            page.Lines.Add(OcrLine.FromRect(paragraphs[i], rect, 1.0));
        }
        return page;
    }

    internal static List<string> SplitParagraphs(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var parts = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1)
            parts = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            var t = p.Trim();
            if (t.Length > 0) result.Add(t);
        }
        return result;
    }
}
