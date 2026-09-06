using System.Text.Json;
using System.Text.Json.Serialization;
using IMTReader.Core.Models;

namespace IMTReader.Core.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _path;

    public AppSettings Current { get; private set; }

    public event Action<AppSettings>? Changed;

    private SettingsStore(string path, AppSettings settings)
    {
        _path = path;
        Current = settings;
    }

    public static SettingsStore Load(string path)
    {
        AppSettings settings = new();
        try
        {
            if (File.Exists(path))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch
        {
            settings = new AppSettings();
        }
        return new SettingsStore(path, settings);
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOptions));
    }

    /// <summary>应用修改、保存并通知订阅者。</summary>
    public void Update(Action<AppSettings> apply)
    {
        var copy = Current.Clone();
        apply(copy);
        Current = copy;
        Save();
        Changed?.Invoke(Current);
    }
}
