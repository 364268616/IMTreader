using System.Buffers;
using System.Text;

namespace IMTReader.Core.Text;

/// <summary>
/// 繁简与异体字转换。字表来自 OpenCC（Apache-2.0）：t2s.txt 为繁→简单字表，variants.txt 为异体/俗字→规范字表。
/// 用户可在数据目录放置 variants.user.txt（每行“异体字 规范字”）进行扩展。
/// </summary>
public static class ChineseConverter
{
    private static readonly Lazy<Dictionary<int, string>> T2S = new(() => LoadEmbedded("IMTReader.Core.Resources.t2s.txt"));
    private static readonly Lazy<Dictionary<int, string>> Variants = new(() => LoadEmbedded("IMTReader.Core.Resources.variants.txt"));
    private static readonly object UserGate = new();
    private static Dictionary<int, string> _userVariants = new();

    private static Dictionary<int, string> LoadEmbedded(string name)
    {
        using var stream = typeof(ChineseConverter).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException("缺少内嵌资源：" + name);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return ParseTable(reader);
    }

    internal static Dictionary<int, string> ParseTable(TextReader reader)
    {
        var dict = new Dictionary<int, string>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length < 2 || line.StartsWith('#')) continue;
            if (Rune.DecodeFromUtf16(line, out var key, out int consumed) != OperationStatus.Done) continue;
            var value = line[consumed..].Trim();
            if (value.Length == 0) continue;
            dict[key.Value] = value;
        }
        return dict;
    }

    /// <summary>加载用户自定义异体字表（可多次调用，后加载者覆盖）。</summary>
    public static void LoadUserVariants(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            using var reader = new StreamReader(path, Encoding.UTF8);
            var table = ParseTable(reader);
            lock (UserGate) _userVariants = table;
        }
        catch
        {
            // 用户表格式错误时忽略
        }
    }

    public static int TraditionalTableSize => T2S.Value.Count;

    /// <summary>繁体 → 简体（逐字）。</summary>
    public static string ToSimplified(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var table = T2S.Value;
        var sb = new StringBuilder(text.Length);
        foreach (var r in text.EnumerateRunes())
        {
            if (table.TryGetValue(r.Value, out var v)) sb.Append(v);
            else sb.Append(r.ToString());
        }
        return sb.ToString();
    }

    /// <summary>异体字 → 规范字（不做繁简转换）。</summary>
    public static string NormalizeVariants(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new StringBuilder(text.Length);
        foreach (var r in text.EnumerateRunes())
        {
            var mapped = MapVariant(r.Value);
            if (mapped != null) sb.Append(mapped);
            else sb.Append(r.ToString());
        }
        return sb.ToString();
    }

    /// <summary>检索归一化：异体字 → 规范字 → 简体。</summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new StringBuilder(text.Length);
        foreach (var r in text.EnumerateRunes())
        {
            var mapped = NormalizeRune(r);
            if (mapped != null) sb.Append(mapped);
            else sb.Append(r.ToString());
        }
        return sb.ToString();
    }

    /// <summary>对单个码位做“异体→规范→简体”映射；无变化时返回 null。</summary>
    public static string? NormalizeRune(Rune r)
    {
        var variant = MapVariant(r.Value);
        var t2s = T2S.Value;
        if (variant == null)
            return t2s.TryGetValue(r.Value, out var s) ? s : null;

        // 异体字映射结果可能不止一个码位（如“廿”→“二十”），再逐字做繁简转换
        var sb = new StringBuilder(variant.Length);
        foreach (var vr in variant.EnumerateRunes())
            sb.Append(t2s.TryGetValue(vr.Value, out var s2) ? s2 : vr.ToString());
        return sb.ToString();
    }

    private static string? MapVariant(int rune)
    {
        Dictionary<int, string> user;
        lock (UserGate) user = _userVariants;
        if (user.Count > 0 && user.TryGetValue(rune, out var u)) return u;
        return Variants.Value.TryGetValue(rune, out var v) ? v : null;
    }

    public static bool ContainsTraditional(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var table = T2S.Value;
        foreach (var r in text.EnumerateRunes())
            if (table.ContainsKey(r.Value)) return true;
        return false;
    }
}
