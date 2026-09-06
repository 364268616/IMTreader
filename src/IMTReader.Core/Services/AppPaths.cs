namespace IMTReader.Core.Services;

public static class AppPaths
{
    /// <summary>数据目录。可用环境变量 IMTREADER_DATA_DIR 覆盖（便于便携使用或测试）。</summary>
    public static string DataDir { get; } =
        Environment.GetEnvironmentVariable("IMTREADER_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IMTReader");

    public static string DatabasePath => Path.Combine(DataDir, "library.db");
    public static string SettingsPath => Path.Combine(DataDir, "settings.json");
    public static string LogDir => Path.Combine(DataDir, "logs");
    public static string LogPath => Path.Combine(LogDir, $"imtreader-{DateTime.Now:yyyyMMdd}.log");
    public static string UserVariantsPath => Path.Combine(DataDir, "variants.user.txt");
    public static string UserErasPath => Path.Combine(DataDir, "eras.user.txt");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogDir);
    }
}
