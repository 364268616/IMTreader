using IMTReader.Core.Ai;
using IMTReader.Core.Data;
using IMTReader.Core.Imaging;
using IMTReader.Core.Ocr;
using IMTReader.Core.Services;
using IMTReader.Core.Text;

namespace IMTReader.App;

/// <summary>应用级服务容器（单例）。</summary>
public static class AppServices
{
    public static SettingsStore Settings { get; private set; } = null!;
    public static LibraryDatabase Db { get; private set; } = null!;
    public static Logger Log { get; private set; } = null!;
    public static AiClient Ai { get; private set; } = null!;
    public static OcrEngineFactory Engines { get; private set; } = null!;
    public static OcrService Ocr { get; private set; } = null!;
    public static OcrJobQueue Jobs { get; private set; } = null!;
    public static TtsService Tts { get; private set; } = null!;

    public static bool IsInitialized { get; private set; }

    public static void Initialize()
    {
        AppPaths.EnsureCreated();
        Log = new Logger(AppPaths.LogPath);
        Logger.Instance = Log;
        Settings = SettingsStore.Load(AppPaths.SettingsPath);
        Db = new LibraryDatabase(AppPaths.DatabasePath);
        Ai = new AiClient(() => Settings.Current, Log);
        Engines = new OcrEngineFactory(Settings, Ai);
        Ocr = new OcrService(Db, Engines, Settings, Log);
        Jobs = new OcrJobQueue(Ocr, PageSourceFactory.Open, Log);
        Tts = new TtsService();

        ChineseConverter.LoadUserVariants(AppPaths.UserVariantsPath);
        EraCalendar.LoadUserEras(AppPaths.UserErasPath);

        IsInitialized = true;
        Log.Info($"应用启动，数据目录：{AppPaths.DataDir}");
        if (!PaddleOcrEngine.IsRuntimeAvailable())
            Log.Warn("未找到 PaddleOCR 运行时或模型目录（inference），本地识别不可用；请检查程序目录是否完整。");
    }

    public static void Shutdown()
    {
        if (!IsInitialized) return;
        try { Jobs.Dispose(); } catch { }
        try { Engines.Dispose(); } catch { }
        try { Tts.Dispose(); } catch { }
        try { Db.Dispose(); } catch { }
        Log.Info("应用退出");
    }
}
