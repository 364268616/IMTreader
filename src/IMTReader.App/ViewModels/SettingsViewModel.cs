using CommunityToolkit.Mvvm.ComponentModel;
using IMTReader.Core.Models;
using IMTReader.Core.Ocr;

namespace IMTReader.App.ViewModels;

/// <summary>设置页视图模型：编辑副本，点击保存后写回。</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private AppSettings _s = AppServices.Settings.Current.Clone();
    private string _testResult = string.Empty;

    public void Reload()
    {
        _s = AppServices.Settings.Current.Clone();
        OnPropertyChanged(string.Empty);
    }

    public void Save()
    {
        var copy = _s.Clone();
        AppServices.Settings.Update(s =>
        {
            s.OcrEngine = copy.OcrEngine;
            s.OcrModelDirectory = copy.OcrModelDirectory;
            s.OcrThreads = copy.OcrThreads;
            s.OcrBatchSize = copy.OcrBatchSize;
            s.UseAngleClassifier = copy.UseAngleClassifier;
            s.OcrMaxSideLen = copy.OcrMaxSideLen;
            s.OcrMinScore = copy.OcrMinScore;
            s.OcrDpi = copy.OcrDpi;
            s.MergeLinesIntoBlocks = copy.MergeLinesIntoBlocks;
            s.WindowsOcrLanguage = copy.WindowsOcrLanguage;
            s.RubbingBinarize = copy.RubbingBinarize;
            s.RenderDpi = copy.RenderDpi;
            s.FontFamily = copy.FontFamily;
            s.FontSize = copy.FontSize;
            s.Theme = copy.Theme;
            s.DefaultHighlightColor = copy.DefaultHighlightColor;
            s.ShowBlockBoxes = copy.ShowBlockBoxes;
            s.SearchIgnoreVariants = copy.SearchIgnoreVariants;
            s.AiEnabled = copy.AiEnabled;
            s.AiBaseUrl = copy.AiBaseUrl;
            s.AiApiKey = copy.AiApiKey;
            s.AiModel = copy.AiModel;
            s.AiVisionModel = copy.AiVisionModel;
            s.DictionaryUrlTemplate = copy.DictionaryUrlTemplate;
        });
        AppServices.Log.Info("设置已保存");
    }

    // ---- OCR ----
    public int OcrEngineIndex { get => (int)_s.OcrEngine; set { _s.OcrEngine = (OcrEngineKind)value; OnPropertyChanged(); } }
    public string OcrModelDirectory { get => _s.OcrModelDirectory; set { _s.OcrModelDirectory = value; OnPropertyChanged(); } }
    public int OcrThreads { get => _s.OcrThreads; set { _s.OcrThreads = Math.Clamp(value, 1, 64); OnPropertyChanged(); } }
    public int OcrBatchSize { get => _s.OcrBatchSize; set { _s.OcrBatchSize = Math.Clamp(value, 1, 256); OnPropertyChanged(); } }
    public bool UseAngleClassifier { get => _s.UseAngleClassifier; set { _s.UseAngleClassifier = value; OnPropertyChanged(); } }
    public int OcrMaxSideLen { get => _s.OcrMaxSideLen; set { _s.OcrMaxSideLen = Math.Clamp(value, 480, 8192); OnPropertyChanged(); } }
    public double OcrMinScore { get => _s.OcrMinScore; set { _s.OcrMinScore = Math.Clamp(value, 0, 1); OnPropertyChanged(); } }
    public int OcrDpi { get => _s.OcrDpi; set { _s.OcrDpi = Math.Clamp(value, 72, 600); OnPropertyChanged(); } }
    public bool MergeLinesIntoBlocks { get => _s.MergeLinesIntoBlocks; set { _s.MergeLinesIntoBlocks = value; OnPropertyChanged(); } }
    public string WindowsOcrLanguage { get => _s.WindowsOcrLanguage; set { _s.WindowsOcrLanguage = value; OnPropertyChanged(); } }
    public bool RubbingBinarize { get => _s.RubbingBinarize; set { _s.RubbingBinarize = value; OnPropertyChanged(); } }

    // ---- 显示 ----
    public int RenderDpi { get => _s.RenderDpi; set { _s.RenderDpi = Math.Clamp(value, 72, 400); OnPropertyChanged(); } }
    public string FontFamily { get => _s.FontFamily; set { _s.FontFamily = value; OnPropertyChanged(); } }
    public double FontSize { get => _s.FontSize; set { _s.FontSize = Math.Clamp(value, 9, 48); OnPropertyChanged(); } }
    public int ThemeIndex { get => (int)_s.Theme; set { _s.Theme = (AppTheme)value; OnPropertyChanged(); } }
    public string DefaultHighlightColor { get => _s.DefaultHighlightColor; set { _s.DefaultHighlightColor = value; OnPropertyChanged(); } }
    public bool ShowBlockBoxes { get => _s.ShowBlockBoxes; set { _s.ShowBlockBoxes = value; OnPropertyChanged(); } }

    // ---- 检索 ----
    public bool SearchIgnoreVariants { get => _s.SearchIgnoreVariants; set { _s.SearchIgnoreVariants = value; OnPropertyChanged(); } }

    // ---- AI ----
    public bool AiEnabled { get => _s.AiEnabled; set { _s.AiEnabled = value; OnPropertyChanged(); } }
    public string AiBaseUrl { get => _s.AiBaseUrl; set { _s.AiBaseUrl = value; OnPropertyChanged(); } }
    public string AiApiKey { get => _s.AiApiKey; set { _s.AiApiKey = value; OnPropertyChanged(); } }
    public string AiModel { get => _s.AiModel; set { _s.AiModel = value; OnPropertyChanged(); } }
    public string AiVisionModel { get => _s.AiVisionModel; set { _s.AiVisionModel = value; OnPropertyChanged(); } }

    // ---- 工具 ----
    public string DictionaryUrlTemplate { get => _s.DictionaryUrlTemplate; set { _s.DictionaryUrlTemplate = value; OnPropertyChanged(); } }

    public string TestResult
    {
        get => _testResult;
        set => SetProperty(ref _testResult, value);
    }

    public string RuntimeInfo
    {
        get
        {
            var paddle = PaddleOcrEngine.IsRuntimeAvailable(_s.OcrModelDirectory) ? "已就绪" : "缺失（请检查模型目录与程序目录）";
            var win = string.Join("、", WindowsOcrEngine.AvailableLanguages());
            return $"PaddleOCR 运行时：{paddle}（模型：{(string.IsNullOrWhiteSpace(_s.OcrModelDirectory) ? PaddleOcrEngine.DefaultModelDirectory : _s.OcrModelDirectory)}）\n" +
                   $"Windows OCR 语言包：{(win.Length == 0 ? "无" : win)}\n数据目录：{Core.Services.AppPaths.DataDir}";
        }
    }

    public IReadOnlyList<string> FontChoices { get; } = new[] { "楷体", "宋体", "仿宋", "黑体", "微软雅黑", "华文楷体", "华文宋体", "华文仿宋", "Microsoft YaHei", "SimSun" };

    public IReadOnlyList<string> AiPresets { get; } = new[]
    {
        "DeepSeek|https://api.deepseek.com/v1|deepseek-chat|",
        "通义千问|https://dashscope.aliyuncs.com/compatible-mode/v1|qwen-plus|qwen-vl-max",
        "智谱|https://open.bigmodel.cn/api/paas/v4|glm-4-plus|glm-4v-plus",
        "月之暗面|https://api.moonshot.cn/v1|moonshot-v1-32k|",
        "Ollama 本地|http://localhost:11434/v1|qwen2.5:14b|qwen2.5vl:7b",
        "OpenAI|https://api.openai.com/v1|gpt-4o-mini|gpt-4o"
    };

    public void ApplyPreset(string preset)
    {
        var parts = preset.Split('|');
        if (parts.Length < 4) return;
        AiBaseUrl = parts[1];
        AiModel = parts[2];
        AiVisionModel = parts[3];
        AiEnabled = true;
    }
}
