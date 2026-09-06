namespace IMTReader.Core.Models;

public enum OcrEngineKind
{
    /// <summary>本地 PaddleOCR（默认，离线，支持竖排与繁体）。</summary>
    Paddle = 0,

    /// <summary>Windows 内置 OCR（仅横排，依赖系统语言包）。</summary>
    WindowsBuiltIn = 1,

    /// <summary>通过 OpenAI 兼容接口调用视觉大模型识别（适合复杂版式、双行小注）。</summary>
    VisionLlm = 2
}

public enum AppTheme
{
    Paper = 0,
    Light = 1,
    Dark = 2
}

public sealed class AppSettings
{
    // ---- OCR ----
    public OcrEngineKind OcrEngine { get; set; } = OcrEngineKind.Paddle;

    /// <summary>PP-OCR 模型所在目录；留空使用程序目录下随附的官方 PP-OCRv5 模型。</summary>
    public string OcrModelDirectory { get; set; } = string.Empty;

    public int OcrThreads { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 2, 16);

    /// <summary>识别阶段一次送入模型的文本行数量；越大越快，显存/内存占用也越高。</summary>
    public int OcrBatchSize { get; set; } = 32;

    public bool UseAngleClassifier { get; set; } = false;

    /// <summary>检测阶段图像长边上限；密集小字（报刊）建议 2048 以上。</summary>
    public int OcrMaxSideLen { get; set; } = 2048;

    public double OcrMinScore { get; set; } = 0.3;

    /// <summary>PDF 页面用于 OCR 的渲染分辨率。</summary>
    public int OcrDpi { get; set; } = 200;

    public bool MergeLinesIntoBlocks { get; set; } = true;
    public string WindowsOcrLanguage { get; set; } = "auto";

    /// <summary>拓片识别时是否额外做二值化。</summary>
    public bool RubbingBinarize { get; set; } = false;

    // ---- 显示 ----
    public int RenderDpi { get; set; } = 150;
    public string FontFamily { get; set; } = "楷体";
    public double FontSize { get; set; } = 17;
    public AppTheme Theme { get; set; } = AppTheme.Paper;
    public string DefaultHighlightColor { get; set; } = "#FFF176";
    public bool ShowBlockBoxes { get; set; } = true;

    // ---- 检索 ----
    public bool SearchIgnoreVariants { get; set; } = true;

    // ---- AI（OpenAI 兼容接口）----
    public bool AiEnabled { get; set; } = false;
    public string AiBaseUrl { get; set; } = "https://api.deepseek.com/v1";
    public string AiApiKey { get; set; } = string.Empty;
    public string AiModel { get; set; } = "deepseek-chat";

    /// <summary>用于图片识别的视觉模型；留空则沿用 AiModel。</summary>
    public string AiVisionModel { get; set; } = string.Empty;

    // ---- 工具 ----
    public string DictionaryUrlTemplate { get; set; } = "https://www.zdic.net/hans/{0}";

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
