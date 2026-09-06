using System.Text;

namespace IMTReader.Core.Text;

/// <summary>
/// 检索用归一化文本：去掉空白与换行、折叠全角 ASCII、可选异体字/繁简归一，并保留到原文字符偏移的映射，
/// 以便把归一化文本中的命中位置映射回原文进行高亮。
/// </summary>
public sealed class NormalizedText
{
    public string Text { get; }

    /// <summary>归一化文本每个字符对应的原文起始偏移。</summary>
    public int[] StartMap { get; }

    /// <summary>归一化文本每个字符对应的原文结束偏移（不含）。</summary>
    public int[] EndMap { get; }

    private NormalizedText(string text, int[] startMap, int[] endMap)
    {
        Text = text;
        StartMap = startMap;
        EndMap = endMap;
    }

    public static NormalizedText Create(string original, bool normalizeChinese, bool dropWhitespace = true)
    {
        original ??= string.Empty;
        var sb = new StringBuilder(original.Length);
        var starts = new List<int>(original.Length);
        var ends = new List<int>(original.Length);
        int index = 0;
        foreach (var r in original.EnumerateRunes())
        {
            int len = r.Utf16SequenceLength;
            if (dropWhitespace && Rune.IsWhiteSpace(r))
            {
                index += len;
                continue;
            }
            string mapped = normalizeChinese ? ChineseConverter.NormalizeRune(r) ?? r.ToString() : r.ToString();
            mapped = FoldWidth(mapped);
            foreach (char c in mapped)
            {
                sb.Append(c);
                starts.Add(index);
                ends.Add(index + len);
            }
            index += len;
        }
        return new NormalizedText(sb.ToString(), starts.ToArray(), ends.ToArray());
    }

    /// <summary>全角 ASCII → 半角，并把拉丁字母折叠为小写。</summary>
    public static string FoldWidth(string s)
    {
        if (s.Length == 1)
        {
            char c = s[0];
            char f = FoldChar(c);
            return f == c ? s : f.ToString();
        }
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) sb.Append(FoldChar(c));
        return sb.ToString();
    }

    private static char FoldChar(char c)
    {
        if (c >= 0xFF01 && c <= 0xFF5E) c = (char)(c - 0xFEE0);
        if (c >= 'A' && c <= 'Z') c = (char)(c + 32);
        return c;
    }

    /// <summary>把归一化文本中的区间映射回原文区间。</summary>
    public (int Start, int Length) ToOriginalRange(int normStart, int normLength)
    {
        if (Text.Length == 0 || normLength <= 0) return (0, 0);
        normStart = Math.Clamp(normStart, 0, Text.Length - 1);
        int last = Math.Clamp(normStart + normLength - 1, normStart, Text.Length - 1);
        int s = StartMap[normStart];
        int e = EndMap[last];
        return (s, Math.Max(0, e - s));
    }
}
