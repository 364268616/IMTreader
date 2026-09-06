using System.Text.RegularExpressions;
using IMTReader.Core.Models;

namespace IMTReader.Core.Text;

/// <summary>全文检索索引：对文本块预先归一化，支持简繁互通、异体字互通与正则。</summary>
public sealed class SearchIndex
{
    private sealed class Entry
    {
        public required OcrBlock Block { get; init; }
        public required string DocumentTitle { get; init; }
        public required NormalizedText Norm { get; init; }
    }

    private readonly List<Entry> _entries = new();
    private readonly bool _normalizeChinese;

    public int BlockCount => _entries.Count;

    private SearchIndex(bool normalizeChinese)
    {
        _normalizeChinese = normalizeChinese;
    }

    public static SearchIndex Build(IEnumerable<(OcrBlock Block, string DocumentTitle)> blocks, bool normalizeChinese)
    {
        var index = new SearchIndex(normalizeChinese);
        foreach (var (block, title) in blocks)
        {
            if (string.IsNullOrWhiteSpace(block.Text)) continue;
            index._entries.Add(new Entry
            {
                Block = block,
                DocumentTitle = title,
                Norm = NormalizedText.Create(block.Text, normalizeChinese)
            });
        }
        return index;
    }

    /// <summary>更新某个块的文本（编辑保存后调用）。</summary>
    public void Update(OcrBlock block)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Block.Id == block.Id)
            {
                _entries[i] = new Entry { Block = block, DocumentTitle = _entries[i].DocumentTitle, Norm = NormalizedText.Create(block.Text, _normalizeChinese) };
                return;
            }
        }
    }

    public SearchResult Search(string keyword, bool useRegex, int snippetContext = 18)
    {
        var result = new SearchResult { Keyword = keyword ?? string.Empty };
        var normKey = PrepareKeyword(keyword ?? string.Empty, _normalizeChinese, useRegex);
        result.NormalizedKeyword = normKey;
        if (normKey.Length == 0) return result;

        Regex? regex = null;
        if (useRegex)
        {
            try { regex = new Regex(normKey, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)); }
            catch (ArgumentException ex)
            {
                result.Error = "正则表达式无效：" + ex.Message;
                return result;
            }
        }

        var pages = new HashSet<(long, int)>();
        foreach (var e in _entries)
        {
            var ranges = FindNormalizedRanges(e.Norm.Text, normKey, regex);
            if (ranges.Count == 0) continue;
            var (s, len) = e.Norm.ToOriginalRange(ranges[0].Start, ranges[0].Length);
            result.Hits.Add(new SearchHit
            {
                DocumentId = e.Block.DocumentId,
                DocumentTitle = e.DocumentTitle,
                PageIndex = e.Block.PageIndex,
                BlockId = e.Block.Id,
                BlockOrder = e.Block.Order,
                Count = ranges.Count,
                Snippet = MakeSnippet(e.Block.Text, s, len, snippetContext)
            });
            result.TotalMatches += ranges.Count;
            pages.Add((e.Block.DocumentId, e.Block.PageIndex));
        }
        result.PageCount = pages.Count;
        return result;
    }

    /// <summary>在一段原文中查找关键词的全部命中区间（原文偏移），用于跳转后高亮。</summary>
    public static IReadOnlyList<(int Start, int Length)> FindRanges(string text, string keyword, bool normalizeChinese, bool useRegex)
    {
        var norm = NormalizedText.Create(text ?? string.Empty, normalizeChinese);
        var normKey = PrepareKeyword(keyword ?? string.Empty, normalizeChinese, useRegex);
        if (normKey.Length == 0 || norm.Text.Length == 0) return Array.Empty<(int, int)>();
        Regex? regex = null;
        if (useRegex)
        {
            try { regex = new Regex(normKey, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)); }
            catch (ArgumentException) { return Array.Empty<(int, int)>(); }
        }
        return FindNormalizedRanges(norm.Text, normKey, regex)
            .Select(r => norm.ToOriginalRange(r.Start, r.Length))
            .Where(r => r.Length > 0)
            .ToList();
    }

    internal static string PrepareKeyword(string keyword, bool normalizeChinese, bool useRegex)
    {
        if (useRegex)
            return normalizeChinese ? ChineseConverter.Normalize(keyword.Trim()) : keyword.Trim();
        return NormalizedText.Create(keyword, normalizeChinese).Text;
    }

    private static List<(int Start, int Length)> FindNormalizedRanges(string text, string key, Regex? regex)
    {
        var list = new List<(int, int)>();
        if (regex != null)
        {
            try
            {
                foreach (Match m in regex.Matches(text))
                    if (m.Length > 0) list.Add((m.Index, m.Length));
            }
            catch (RegexMatchTimeoutException)
            {
                // 超时视为无命中
            }
            return list;
        }
        int pos = 0;
        while (pos <= text.Length - key.Length)
        {
            int i = text.IndexOf(key, pos, StringComparison.Ordinal);
            if (i < 0) break;
            list.Add((i, key.Length));
            pos = i + Math.Max(1, key.Length);
        }
        return list;
    }

    internal static string MakeSnippet(string text, int start, int length, int context)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        int from = Math.Max(0, start - context);
        int to = Math.Min(text.Length, start + length + context);
        var s = text[from..to].Replace("\r", string.Empty).Replace('\n', ' ');
        if (from > 0) s = "…" + s;
        if (to < text.Length) s += "…";
        return s;
    }
}
