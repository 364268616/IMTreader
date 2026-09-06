using System.Text;
using IMTReader.Core.Models;
using IMTReader.Core.Text;

namespace IMTReader.Core.Export;

public static class Exporter
{
    private static readonly UTF8Encoding Utf8Bom = new(true);

    public static void WriteText(string path, string content) => File.WriteAllText(path, content, Utf8Bom);

    /// <summary>导出全文（TXT 或 Markdown）。</summary>
    /// <param name="joinLines">把块内的行（竖排的各栏）合并为一段，即“竖排转横排”后连成整段。</param>
    public static string BuildFullText(DocumentInfo doc, IReadOnlyList<OcrBlock> blocks, bool markdown, bool pageMarkers, bool simplified, bool joinLines)
    {
        var sb = new StringBuilder();
        if (markdown) sb.Append("# ").AppendLine(doc.Title).AppendLine();
        else sb.AppendLine(doc.Title).AppendLine();

        foreach (var pageGroup in blocks.GroupBy(b => b.PageIndex).OrderBy(g => g.Key))
        {
            if (pageMarkers)
            {
                if (markdown) sb.Append("## 第 ").Append(pageGroup.Key + 1).AppendLine(" 页").AppendLine();
                else sb.Append("【第 ").Append(pageGroup.Key + 1).AppendLine(" 页】").AppendLine();
            }
            foreach (var b in pageGroup.OrderBy(b => b.Order))
            {
                var text = b.Text;
                if (simplified) text = ChineseConverter.ToSimplified(text);
                if (joinLines) text = text.Replace("\r", string.Empty).Replace("\n", string.Empty);
                sb.AppendLine(text).AppendLine();
            }
        }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string BuildSearchCsv(SearchResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("文献,页码,块号,次数,片段");
        foreach (var h in r.Hits)
            sb.Append(Csv(h.DocumentTitle)).Append(',').Append(h.PageNumber).Append(',').Append(h.BlockNumber).Append(',')
              .Append(h.Count).Append(',').AppendLine(Csv(h.Snippet));
        return sb.ToString();
    }

    public static string BuildSearchText(SearchResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"关键词：{r.Keyword}　结果：{r.Hits.Count} 条　命中：{r.TotalMatches} 次　页数：{r.PageCount} 页");
        sb.AppendLine();
        foreach (var h in r.Hits)
            sb.AppendLine($"《{h.DocumentTitle}》 第 {h.PageNumber} 页 第 {h.BlockNumber} 块（{h.Count} 次）：{h.Snippet}");
        return sb.ToString();
    }

    public static string BuildHighlightsMarkdown(DocumentInfo doc, IReadOnlyList<Highlight> highlights, IReadOnlyDictionary<long, OcrBlock>? blocks = null)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(doc.Title).AppendLine(" 高亮摘录").AppendLine();
        foreach (var g in highlights.GroupBy(h => h.PageIndex).OrderBy(g => g.Key))
        {
            sb.Append("## 第 ").Append(g.Key + 1).AppendLine(" 页").AppendLine();
            foreach (var h in g.OrderBy(h => h.BlockId).ThenBy(h => h.Start))
            {
                int? order = blocks != null && blocks.TryGetValue(h.BlockId, out var b) ? b.Order : null;
                sb.Append("- ");
                if (order != null) sb.Append("[块 ").Append(order.Value + 1).Append("] ");
                sb.AppendLine(h.Excerpt.Replace("\n", " "));
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string BuildCitation(DocumentInfo doc, int pageIndex, int? blockOrder = null)
    {
        var s = $"《{doc.Title}》第 {pageIndex + 1} 页";
        if (blockOrder != null) s += $"，第 {blockOrder.Value + 1} 块";
        return s;
    }

    private static string Csv(string s)
    {
        s ??= string.Empty;
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
