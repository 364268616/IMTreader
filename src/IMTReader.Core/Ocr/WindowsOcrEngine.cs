using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using IMTReader.Core.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;
using WinBitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;
using BitmapPixelFormat = Windows.Graphics.Imaging.BitmapPixelFormat;
using BitmapAlphaMode = Windows.Graphics.Imaging.BitmapAlphaMode;

namespace IMTReader.Core.Ocr;

/// <summary>Windows 10/11 内置 OCR。无需下载模型，但仅支持横排文本，识别质量依赖系统语言包。</summary>
public sealed class WindowsOcrEngine : IOcrEngine
{
    private readonly string _languagePreference;

    public WindowsOcrEngine(string languagePreference)
    {
        _languagePreference = languagePreference ?? "auto";
    }

    public string Name => "Windows OCR";

    public static IReadOnlyList<string> AvailableLanguages()
    {
        try
        {
            return OcrEngine.AvailableRecognizerLanguages.Select(l => $"{l.LanguageTag} ({l.DisplayName})").ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private OcrEngine? CreateEngine()
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(_languagePreference) && _languagePreference != "auto")
            candidates.Add(_languagePreference);
        candidates.AddRange(new[] { "zh-Hant-TW", "zh-Hant-HK", "zh-Hant", "zh-Hans-CN", "zh-Hans", "zh-CN" });
        foreach (var tag in candidates)
        {
            try
            {
                var lang = new Language(tag);
                if (!OcrEngine.IsLanguageSupported(lang)) continue;
                var e = OcrEngine.TryCreateFromLanguage(lang);
                if (e != null) return e;
            }
            catch
            {
                // 无效的语言标记，尝试下一个
            }
        }
        return OcrEngine.TryCreateFromUserProfileLanguages();
    }

    public async Task<OcrPageResult> RecognizeAsync(BitmapSource image, OcrOptions options, CancellationToken ct)
    {
        var engine = CreateEngine() ?? throw new InvalidOperationException("未找到可用的 Windows OCR 语言包，请在“设置 → 时间和语言 → 语言”中添加中文并安装“光学字符识别”功能。");

        double scale = 1.0;
        int maxDim = (int)Math.Min(OcrEngine.MaxImageDimension, 4000u);
        var img = image;
        int longest = Math.Max(image.PixelWidth, image.PixelHeight);
        if (longest > maxDim)
        {
            scale = maxDim / (double)longest;
            img = BitmapUtil.Resize(image, scale);
        }

        var png = BitmapUtil.EncodePng(img);
        using var ms = new MemoryStream(png);
        var ras = ms.AsRandomAccessStream();
        var decoder = await WinBitmapDecoder.CreateAsync(ras);
        using var sb = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        ct.ThrowIfCancellationRequested();
        var res = await engine.RecognizeAsync(sb);

        var page = new OcrPageResult
        {
            EngineName = Name,
            ImageWidth = image.PixelWidth,
            ImageHeight = image.PixelHeight
        };
        foreach (var line in res.Lines)
        {
            if (line.Words.Count == 0) continue;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var w in line.Words)
            {
                var r = w.BoundingRect;
                minX = Math.Min(minX, r.X);
                minY = Math.Min(minY, r.Y);
                maxX = Math.Max(maxX, r.X + r.Width);
                maxY = Math.Max(maxY, r.Y + r.Height);
            }
            var rect = new Rect(minX / scale, minY / scale, (maxX - minX) / scale, (maxY - minY) / scale);
            page.Lines.Add(OcrLine.FromRect(JoinWords(line.Words.Select(w => w.Text)), rect, 1.0));
        }
        return page;
    }

    /// <summary>Windows OCR 把每个汉字当作一个“词”，中间会插入空格；这里把 CJK 字符之间的空格去掉。</summary>
    internal static string JoinWords(IEnumerable<string> words)
    {
        var sb = new StringBuilder();
        foreach (var w in words)
        {
            if (string.IsNullOrEmpty(w)) continue;
            if (sb.Length > 0 && !(IsCjk(sb[^1]) || IsCjk(w[0]))) sb.Append(' ');
            sb.Append(w);
        }
        return sb.ToString();
    }

    internal static bool IsCjk(char c) =>
        (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0x3000 && c <= 0x303F) ||
        (c >= 0xFF00 && c <= 0xFFEF) || (c >= 0xF900 && c <= 0xFAFF) || char.IsSurrogate(c);
}
