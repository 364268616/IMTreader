using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IMTReader.Core.Data;
using IMTReader.Core.Export;
using IMTReader.Core.Imaging;
using IMTReader.Core.Models;
using IMTReader.Core.Ocr;
using IMTReader.Core.Text;
using Xunit;

namespace IMTReader.Tests;

public class ChineseConverterTests
{
    [Fact]
    public void ToSimplified_ConvertsTraditionalCharacters()
    {
        Assert.Equal("学而时习之，不亦说乎", ChineseConverter.ToSimplified("學而時習之，不亦說乎"));
        Assert.Equal("中华人民共和国", ChineseConverter.ToSimplified("中華人民共和國"));
    }

    [Fact]
    public void Normalize_UnifiesVariantsAndTraditional()
    {
        Assert.Equal("为", ChineseConverter.Normalize("爲"));
        Assert.Equal("为", ChineseConverter.Normalize("為"));
        Assert.Equal("二十一", ChineseConverter.Normalize("廿一"));
        Assert.Equal("群众", ChineseConverter.Normalize("羣衆"));
    }

    [Fact]
    public void ContainsTraditional_Detects()
    {
        Assert.True(ChineseConverter.ContainsTraditional("識別文本"));
        Assert.False(ChineseConverter.ContainsTraditional("识别文本"));
    }
}

public class NormalizedTextTests
{
    [Fact]
    public void MapsBackToOriginalOffsets()
    {
        var norm = NormalizedText.Create("學而\n時習之", true);
        Assert.Equal("学而时习之", norm.Text);
        var (start, len) = norm.ToOriginalRange(2, 2);
        Assert.Equal(3, start);
        Assert.Equal(2, len);
        Assert.Equal("時習", "學而\n時習之".Substring(start, len));
    }

    [Fact]
    public void FoldsFullWidthAscii()
    {
        Assert.Equal("abc123", NormalizedText.Create("ＡＢＣ１２３", false).Text);
    }
}

public class SearchIndexTests
{
    private static SearchIndex Build(params string[] texts)
    {
        var blocks = texts.Select((t, i) => (new OcrBlock { Id = i + 1, DocumentId = 1, PageIndex = i / 2, Order = i % 2, Text = t }, "测试文献"));
        return SearchIndex.Build(blocks, normalizeChinese: true);
    }

    [Fact]
    public void SimplifiedKeywordMatchesTraditionalText()
    {
        var index = Build("諸人漸規經已寨出此縣", "又云該處所屯之兵", "西國女童在此女學");
        var r = index.Search("渐规", useRegex: false);
        Assert.Single(r.Hits);
        Assert.Equal(1, r.TotalMatches);
        Assert.Equal(1, r.PageCount);
        Assert.Contains("漸規", r.Hits[0].Snippet);
    }

    [Fact]
    public void CountsMultipleMatchesInBlock()
    {
        var index = Build("西國女童在此女學女");
        var r = index.Search("女", useRegex: false);
        Assert.Equal(3, r.TotalMatches);
    }

    [Fact]
    public void RegexSearchWorks()
    {
        var index = Build("光緒二十年", "光緒三十四年", "宣統元年");
        var r = index.Search("光绪.+年", useRegex: true);
        Assert.Equal(2, r.Hits.Count);
    }

    [Fact]
    public void FindRangesReturnsOriginalOffsets()
    {
        var ranges = SearchIndex.FindRanges("子曰學而\n時習之", "学而时习", true, false);
        Assert.Single(ranges);
        Assert.Equal(2, ranges[0].Start);
        Assert.Equal(5, ranges[0].Length);
    }
}

public class LayoutAnalyzerTests
{
    private static OcrLine Col(string text, double x, double y, double w, double h) =>
        OcrLine.FromRect(text, new Rect(x, y, w, h), 1.0);

    [Fact]
    public void VerticalColumnsAreReadRightToLeftAndMergedIntoBlocks()
    {
        // 与实测 PaddleOCR 输出一致：六栏正文 + 右侧标题栏，检测结果顺序是任意的
        var page = new OcrPageResult { ImageWidth = 1200, ImageHeight = 1000 };
        page.Lines.Add(Col("謀而不忠乎", 678, 60, 42, 781));
        page.Lines.Add(Col("子曰巧言令色", 738, 58, 45, 917));
        page.Lines.Add(Col("也君子務本", 802, 58, 41, 920));
        page.Lines.Add(Col("弟而好犯上者", 862, 58, 45, 920));
        page.Lines.Add(Col("乎人不知而不慍", 925, 58, 45, 920));
        page.Lines.Add(Col("子曰學而時習之", 988, 61, 42, 912));
        page.Lines.Add(Col("論語學而第一", 1078, 61, 50, 302));

        var blocks = LayoutAnalyzer.BuildBlocks(page);

        Assert.Equal(2, blocks.Count);
        Assert.Equal("論語學而第一", blocks[0].Text);
        Assert.True(blocks[1].IsVertical);
        var lines = blocks[1].Text.Split('\n');
        Assert.Equal(6, lines.Length);
        Assert.Equal("子曰學而時習之", lines[0]);
        Assert.Equal("謀而不忠乎", lines[^1]);
    }

    [Fact]
    public void HorizontalLinesAreReadTopToBottom()
    {
        var page = new OcrPageResult { ImageWidth = 1000, ImageHeight = 300 };
        page.Lines.Add(Col("第二行", 40, 90, 690, 32));
        page.Lines.Add(Col("第一行", 40, 39, 670, 34));
        var blocks = LayoutAnalyzer.BuildBlocks(page);
        Assert.Single(blocks);
        Assert.False(blocks[0].IsVertical);
        Assert.Equal("第一行\n第二行", blocks[0].Text);
    }

    [Fact]
    public void WideVerticalGapSplitsHorizontalBlocks()
    {
        var page = new OcrPageResult { ImageWidth = 1000, ImageHeight = 600 };
        page.Lines.Add(Col("段落一", 40, 40, 600, 30));
        page.Lines.Add(Col("段落二", 40, 300, 600, 30));
        var blocks = LayoutAnalyzer.BuildBlocks(page);
        Assert.Equal(2, blocks.Count);
        Assert.Equal("段落一", blocks[0].Text);
    }

    [Fact]
    public void LowScoreLinesAreDropped()
    {
        var page = new OcrPageResult { ImageWidth = 100, ImageHeight = 100 };
        page.Lines.Add(OcrLine.FromRect("垃圾", new Rect(0, 0, 40, 10), 0.1));
        Assert.Empty(LayoutAnalyzer.BuildBlocks(page));
    }
}

public class EraCalendarTests
{
    [Theory]
    [InlineData(1984, "甲子")]
    [InlineData(1894, "甲午")]
    [InlineData(1911, "辛亥")]
    [InlineData(2024, "甲辰")]
    public void GanZhiIsCorrect(int year, string expected) => Assert.Equal(expected, EraCalendar.GanZhi(year));

    [Theory]
    [InlineData("光绪二十年", 1894)]
    [InlineData("光緒甲午", 1894)]
    [InlineData("民国三十八年", 1949)]
    [InlineData("民國卅八年", 1949)]
    [InlineData("乾隆元年", 1736)]
    [InlineData("清康熙六十一年", 1722)]
    [InlineData("萬曆四十八年", 1620)]
    public void ParsesEraYears(string input, int expected)
    {
        var results = EraCalendar.Parse(input);
        Assert.Contains(results, r => r.Year == expected);
    }

    [Fact]
    public void DuplicateEraNamesReturnAllCandidates()
    {
        var results = EraCalendar.Parse("至元二年");
        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r.Year == 1265);
        Assert.Contains(results, r => r.Year == 1336);
    }

    [Fact]
    public void DescribesWesternYear()
    {
        var text = EraCalendar.Describe("1894");
        Assert.Contains("光绪", text);
        Assert.Contains("甲午", text);
    }
}

public class ChineseNumeralTests
{
    [Theory]
    [InlineData("廿一", 21)]
    [InlineData("二十", 20)]
    [InlineData("十", 10)]
    [InlineData("十五", 15)]
    [InlineData("元", 1)]
    [InlineData("一百零五", 105)]
    [InlineData("三十八", 38)]
    [InlineData("卅八", 38)]
    [InlineData("１２", 12)]
    [InlineData("六十一", 61)]
    public void ParsesNumerals(string s, int expected)
    {
        Assert.True(ChineseNumeral.TryParse(s, out int v));
        Assert.Equal(expected, v);
    }

    [Theory]
    [InlineData(1, "元")]
    [InlineData(10, "十")]
    [InlineData(21, "二十一")]
    [InlineData(105, "一百零五")]
    public void FormatsNumerals(int n, string expected) => Assert.Equal(expected, ChineseNumeral.ToChinese(n));
}

public class MarkdownLiteTests
{
    [Fact]
    public void ParsesBasicStructure()
    {
        var blocks = MarkdownLite.Parse("# 标题\n\n段落 **粗体** ==高亮== 结尾\n- 项目一\n1. 第一\n> 引用");
        Assert.IsType<MdHeading>(blocks[0]);
        var p = Assert.IsType<MdParagraph>(blocks[1]);
        Assert.Contains(p.Spans, s => s.Bold && s.Text == "粗体");
        Assert.Contains(p.Spans, s => s.Mark && s.Text == "高亮");
        var li = Assert.IsType<MdListItem>(blocks[2]);
        Assert.False(li.Ordered);
        var ol = Assert.IsType<MdListItem>(blocks[3]);
        Assert.True(ol.Ordered);
        Assert.IsType<MdQuote>(blocks[4]);
    }

    [Fact]
    public void PlainTextStripsMarkup()
    {
        Assert.Equal("标题\n\n粗体 高亮", MarkdownLite.ToPlainText("# 标题\n\n**粗体** ==高亮==").Replace("\r\n", "\n"));
    }
}

public class LibraryDatabaseTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "imtreader-test-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly LibraryDatabase _db;

    public LibraryDatabaseTests()
    {
        _db = new LibraryDatabase(_path);
    }

    [Fact]
    public void DocumentsBlocksHighlightsAndNotesRoundTrip()
    {
        var doc = new DocumentInfo { Title = "测试", Kind = SourceKind.Pdf, SourcePath = @"C:\x.pdf", PageCount = 3 };
        _db.AddDocument(doc);
        Assert.True(doc.Id > 0);

        var blocks = new[]
        {
            new OcrBlock { X = 0.1, Y = 0.1, W = 0.2, H = 0.5, IsVertical = true, Text = "第一块\n第二栏", Score = 0.9 },
            new OcrBlock { X = 0.5, Y = 0.1, W = 0.2, H = 0.5, IsVertical = true, Text = "第二块", Score = 0.8 }
        };
        _db.ReplacePageBlocks(doc.Id, 1, blocks);
        _db.SetPageStatus(doc.Id, 1, OcrStatus.Done, "test");

        var loaded = _db.GetPageBlocks(doc.Id, 1);
        Assert.Equal(2, loaded.Count);
        Assert.Equal("第一块\n第二栏", loaded[0].Text);
        Assert.Equal(0, loaded[0].Order);
        Assert.True(loaded[1].Id > loaded[0].Id);

        _db.AppendPageBlocks(doc.Id, 1, new[] { new OcrBlock { Text = "追加块" } });
        Assert.Equal(3, _db.GetPageBlocks(doc.Id, 1).Count);
        Assert.Equal(2, _db.GetPageBlocks(doc.Id, 1)[2].Order);

        _db.UpdateBlockText(loaded[0].Id, "修改后");
        Assert.Equal("修改后", _db.GetBlock(loaded[0].Id)!.Text);

        _db.ReplaceBlockHighlights(doc.Id, 1, loaded[0].Id, new[] { new Highlight { Start = 0, Length = 2, Color = "#FFF176", Excerpt = "修改" } });
        Assert.Single(_db.GetDocumentHighlights(doc.Id));
        Assert.Single(_db.GetPageHighlights(doc.Id, 1));

        _db.SaveNotes(doc.Id, "# 笔记");
        Assert.Equal("# 笔记", _db.GetNotes(doc.Id));

        var d2 = _db.GetDocument(doc.Id)!;
        Assert.Equal(1, d2.OcrDonePages);
        Assert.Equal(1, _db.CountOcrDonePages(doc.Id));

        _db.TouchDocument(doc.Id, 2);
        Assert.Single(_db.GetRecentDocuments());

        _db.ClearPageHighlights(doc.Id, 1);
        Assert.Empty(_db.GetDocumentHighlights(doc.Id));

        _db.DeleteDocument(doc.Id);
        Assert.Null(_db.GetDocument(doc.Id));
        Assert.Empty(_db.GetPageBlocks(doc.Id, 1));
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_path); } catch { }
    }
}

public class BitmapUtilTests
{
    private static BitmapSource Solid(byte b, byte g, byte r, int w = 4, int h = 4)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i] = b;
            px[i + 1] = g;
            px[i + 2] = r;
            px[i + 3] = 255;
        }
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        bmp.Freeze();
        return bmp;
    }

    private static byte[] Pixels(BitmapSource s)
    {
        var px = new byte[s.PixelWidth * s.PixelHeight * 4];
        BitmapUtil.ToBgra32(s).CopyPixels(px, s.PixelWidth * 4, 0);
        return px;
    }

    [Fact]
    public void InvertFlipsColors()
    {
        var inv = Pixels(BitmapUtil.Invert(Solid(0, 0, 0)));
        Assert.Equal(255, inv[0]);
        Assert.Equal(255, inv[3]);
    }

    [Fact]
    public void BinarizeProducesBlackAndWhite()
    {
        var w = 4;
        var px = new byte[w * w * 4];
        for (int i = 0; i < px.Length; i += 4)
        {
            byte v = (i / 4) % 2 == 0 ? (byte)30 : (byte)220;
            px[i] = px[i + 1] = px[i + 2] = v;
            px[i + 3] = 255;
        }
        var bmp = BitmapSource.Create(w, w, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
        bmp.Freeze();
        var bin = Pixels(BitmapUtil.Binarize(bmp));
        Assert.All(Enumerable.Range(0, w * w).Select(i => bin[i * 4]), v => Assert.True(v == 0 || v == 255));
    }

    [Fact]
    public void ToMatProducesBgrPixels()
    {
        using var mat = BitmapUtil.ToMat(Solid(10, 20, 30, 5, 3));
        Assert.Equal(5, mat.Width);
        Assert.Equal(3, mat.Height);
        Assert.Equal(3, mat.Channels());
        var px = mat.At<OpenCvSharp.Vec3b>(1, 2);
        Assert.Equal(10, px.Item0);
        Assert.Equal(20, px.Item1);
        Assert.Equal(30, px.Item2);
    }

    [Fact]
    public void PngEncodeDecodeRoundTrip()
    {
        var png = BitmapUtil.EncodePng(Solid(1, 2, 3));
        using var ms = new MemoryStream(png);
        var decoded = BitmapUtil.DecodeFrozen(ms);
        Assert.Equal(4, decoded.PixelWidth);
        Assert.True(decoded.IsFrozen);
    }
}

public class MiscTests
{
    [Fact]
    public void WindowsOcrJoinWordsRemovesSpacesBetweenCjk()
    {
        Assert.Equal("中华abc def人民", WindowsOcrEngine.JoinWords(new[] { "中", "华", "abc", "def", "人", "民" }));
    }

    [Fact]
    public void VisionParagraphSplit()
    {
        var parts = VisionLlmOcrEngine.SplitParagraphs("第一段\n\n第二段\r\n\r\n第三段");
        Assert.Equal(3, parts.Count);
        Assert.Equal("第三段", parts[2]);
    }

    [Fact]
    public void ExportFullTextHasPageMarkers()
    {
        var doc = new DocumentInfo { Title = "书" };
        var blocks = new List<OcrBlock>
        {
            new() { PageIndex = 0, Order = 0, Text = "甲\n乙" },
            new() { PageIndex = 1, Order = 0, Text = "丙" }
        };
        var txt = Exporter.BuildFullText(doc, blocks, markdown: false, pageMarkers: true, simplified: false, joinLines: true);
        Assert.Contains("【第 1 页】", txt);
        Assert.Contains("甲乙", txt);
        var md = Exporter.BuildFullText(doc, blocks, markdown: true, pageMarkers: true, simplified: false, joinLines: false);
        Assert.Contains("## 第 2 页", md);
    }

    [Fact]
    public void PageCacheEvictsLeastRecentlyUsed()
    {
        var cache = new PageCache(2);
        var img = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        img.Freeze();
        cache.Put(0, 150, img);
        cache.Put(1, 150, img);
        Assert.True(cache.TryGet(0, 150, out _));
        cache.Put(2, 150, img);
        Assert.False(cache.TryGet(1, 150, out _));
        Assert.True(cache.TryGet(0, 150, out _));
    }
}
