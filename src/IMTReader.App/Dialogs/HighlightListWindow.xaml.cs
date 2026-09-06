using System.Windows;
using System.Windows.Input;
using IMTReader.App.ViewModels;
using IMTReader.App.Views;
using IMTReader.Core.Export;
using IMTReader.Core.Models;
using Microsoft.Win32;

namespace IMTReader.App.Dialogs;

public sealed class HighlightRow
{
    public required Highlight Highlight { get; init; }
    public int PageNumber => Highlight.PageIndex + 1;
    public int BlockNumber { get; init; }
    public string Color => Highlight.Color;
    public string Excerpt => Highlight.Excerpt.Replace("\n", " ");
    public DateTime CreatedAt => Highlight.CreatedAt;
}

public partial class HighlightListWindow : Window
{
    private readonly MainWindow _owner;
    private readonly DocumentViewModel _doc;
    private List<HighlightRow> _rows = new();

    public HighlightListWindow(MainWindow owner, DocumentViewModel doc)
    {
        InitializeComponent();
        _owner = owner;
        _doc = doc;
        Title = $"高亮列表 — {doc.Title}";
        Reload();
    }

    public void Reload()
    {
        var highlights = AppServices.Db.GetDocumentHighlights(_doc.Document.Id);
        var orders = new Dictionary<long, int>();
        foreach (var page in highlights.Select(h => h.PageIndex).Distinct())
            foreach (var b in AppServices.Db.GetPageBlocks(_doc.Document.Id, page))
                orders[b.Id] = b.Order;
        _rows = highlights.Select(h => new HighlightRow { Highlight = h, BlockNumber = (orders.TryGetValue(h.BlockId, out var o) ? o : 0) + 1 }).ToList();
        Grid.ItemsSource = _rows;
        SummaryText.Text = $"共 {_rows.Count} 处高亮，分布于 {highlights.Select(h => h.PageIndex).Distinct().Count()} 页";
    }

    private void Jump()
    {
        if (Grid.SelectedItem is HighlightRow row)
            _owner.JumpTo(_doc.Document.Id, row.Highlight.PageIndex, row.Highlight.BlockId, null);
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Jump();
    private void Jump_Click(object sender, RoutedEventArgs e) => Jump();
    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is not HighlightRow row) return;
        AppServices.Db.DeleteHighlight(row.Highlight.Id);
        if (row.Highlight.PageIndex == _doc.CurrentPage) _doc.LoadBlocks();
        Reload();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Title = "导出高亮", FileName = _doc.Title + "_高亮", Filter = "Markdown (*.md)|*.md|文本文件 (*.txt)|*.txt", DefaultExt = ".md" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var blocks = _rows.ToDictionary(r => r.Highlight.BlockId, r => new OcrBlock { Id = r.Highlight.BlockId, Order = r.BlockNumber - 1 });
            Exporter.WriteText(dlg.FileName, Exporter.BuildHighlightsMarkdown(_doc.Document, _rows.Select(r => r.Highlight).ToList(), blocks));
            SummaryText.Text = "已导出：" + dlg.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message, "导出", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
