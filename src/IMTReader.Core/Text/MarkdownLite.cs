using System.Text;
using System.Text.RegularExpressions;

namespace IMTReader.Core.Text;

public abstract record MdBlock;

public sealed record MdHeading(int Level, IReadOnlyList<MdSpan> Spans) : MdBlock;

public sealed record MdParagraph(IReadOnlyList<MdSpan> Spans) : MdBlock;

public sealed record MdListItem(IReadOnlyList<MdSpan> Spans, bool Ordered, int Indent, string Marker) : MdBlock;

public sealed record MdQuote(IReadOnlyList<MdSpan> Spans) : MdBlock;

public sealed record MdCode(string Text) : MdBlock;

public sealed record MdRule : MdBlock;

/// <summary>行内片段。Mark 表示 ==高亮== 语法。</summary>
public sealed record MdSpan(string Text, bool Bold = false, bool Italic = false, bool Mark = false, bool Code = false);

/// <summary>笔记用的极简 Markdown 解析器：标题、段落、列表、引用、代码块、分隔线，行内粗体/斜体/高亮/代码。</summary>
public static class MarkdownLite
{
    private static readonly Regex OrderedRegex = new(@"^(\s*)(\d+)[.、)]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex BulletRegex = new(@"^(\s*)[-*+]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);

    public static List<MdBlock> Parse(string markdown)
    {
        var blocks = new List<MdBlock>();
        if (string.IsNullOrEmpty(markdown)) return blocks;
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var paragraph = new List<string>();
        bool inCode = false;
        var code = new StringBuilder();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new MdParagraph(ParseInline(string.Join("\n", paragraph))));
            paragraph.Clear();
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (inCode)
            {
                if (line.TrimStart().StartsWith("```"))
                {
                    blocks.Add(new MdCode(code.ToString().TrimEnd('\n')));
                    code.Clear();
                    inCode = false;
                }
                else code.Append(raw).Append('\n');
                continue;
            }
            if (line.TrimStart().StartsWith("```"))
            {
                FlushParagraph();
                inCode = true;
                continue;
            }
            if (line.Trim().Length == 0)
            {
                FlushParagraph();
                continue;
            }
            var trimmed = line.Trim();
            if (trimmed is "---" or "***" or "___")
            {
                FlushParagraph();
                blocks.Add(new MdRule());
                continue;
            }
            var h = HeadingRegex.Match(trimmed);
            if (h.Success)
            {
                FlushParagraph();
                blocks.Add(new MdHeading(h.Groups[1].Value.Length, ParseInline(h.Groups[2].Value)));
                continue;
            }
            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                blocks.Add(new MdQuote(ParseInline(trimmed[1..].TrimStart())));
                continue;
            }
            var b = BulletRegex.Match(line);
            if (b.Success)
            {
                FlushParagraph();
                blocks.Add(new MdListItem(ParseInline(b.Groups[2].Value), false, b.Groups[1].Value.Length / 2, "•"));
                continue;
            }
            var o = OrderedRegex.Match(line);
            if (o.Success)
            {
                FlushParagraph();
                blocks.Add(new MdListItem(ParseInline(o.Groups[3].Value), true, o.Groups[1].Value.Length / 2, o.Groups[2].Value + "."));
                continue;
            }
            paragraph.Add(line);
        }
        if (inCode && code.Length > 0) blocks.Add(new MdCode(code.ToString().TrimEnd('\n')));
        FlushParagraph();
        return blocks;
    }

    public static List<MdSpan> ParseInline(string text)
    {
        var spans = new List<MdSpan>();
        if (string.IsNullOrEmpty(text)) return spans;
        bool bold = false, italic = false, mark = false, codeMode = false;
        var sb = new StringBuilder();

        void Flush()
        {
            if (sb.Length == 0) return;
            spans.Add(new MdSpan(sb.ToString(), bold, italic, mark, codeMode));
            sb.Clear();
        }

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '`')
            {
                Flush();
                codeMode = !codeMode;
                i++;
                continue;
            }
            if (codeMode)
            {
                sb.Append(c);
                i++;
                continue;
            }
            if (c == '\\' && i + 1 < text.Length)
            {
                sb.Append(text[i + 1]);
                i += 2;
                continue;
            }
            if (c == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                Flush();
                bold = !bold;
                i += 2;
                continue;
            }
            if (c == '=' && i + 1 < text.Length && text[i + 1] == '=')
            {
                Flush();
                mark = !mark;
                i += 2;
                continue;
            }
            if (c == '*' || c == '_')
            {
                // 单个 * 或 _ 作为斜体标记，但字词中间的下划线不处理
                bool boundary = i == 0 || i == text.Length - 1 || !(char.IsLetterOrDigit(text[i - 1]) && i + 1 < text.Length && char.IsLetterOrDigit(text[i + 1]));
                if (c == '*' || boundary)
                {
                    Flush();
                    italic = !italic;
                    i++;
                    continue;
                }
            }
            sb.Append(c);
            i++;
        }
        Flush();
        return spans;
    }

    /// <summary>去掉 Markdown 标记的纯文本（用于导出 TXT）。</summary>
    public static string ToPlainText(string markdown)
    {
        var sb = new StringBuilder();
        foreach (var block in Parse(markdown))
        {
            switch (block)
            {
                case MdHeading h: sb.AppendLine(Join(h.Spans)); break;
                case MdParagraph p: sb.AppendLine(Join(p.Spans)); break;
                case MdListItem li: sb.Append(' ', li.Indent * 2).Append(li.Marker).Append(' ').AppendLine(Join(li.Spans)); break;
                case MdQuote q: sb.Append("　").AppendLine(Join(q.Spans)); break;
                case MdCode c: sb.AppendLine(c.Text); break;
                case MdRule: sb.AppendLine("――――――――"); break;
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static string Join(IReadOnlyList<MdSpan> spans) => string.Concat(spans.Select(s => s.Text));
}
