using System.Windows;
using System.Windows.Input;
using IMTReader.App.ViewModels;
using IMTReader.App.Views;
using IMTReader.Core.Export;
using IMTReader.Core.Models;
using IMTReader.Core.Text;
using Microsoft.Win32;

namespace IMTReader.App.Dialogs;

public partial class SearchWindow : Window
{
    private readonly MainWindow _owner;
    private readonly DocumentViewModel? _doc;
    private SearchIndex? _index;
    private bool _indexIsLibrary;
    private bool _indexNormalized;
    private int _indexVersion = -1;
    private SearchResult? _result;

    public SearchWindow(MainWindow owner, DocumentViewModel? doc)
    {
        InitializeComponent();
        _owner = owner;
        _doc = doc;
        NormalizeBox.IsChecked = AppServices.Settings.Current.SearchIgnoreVariants;
        if (doc == null) LibraryBox.IsChecked = true;
        Title = doc == null ? "全文搜索（文献库）" : $"全文搜索 — {doc.Title}";
        AppServices.Jobs.PageCompleted += OnPageCompleted;
    }

    public void FocusInput()
    {
        KeywordBox.Focus();
        KeywordBox.SelectAll();
    }

    private void OnPageCompleted(Core.Ocr.OcrJob job, int page, IReadOnlyList<OcrBlock> blocks) => _indexVersion = -1;

    private SearchIndex GetIndex(bool library, bool normalize)
    {
        int version = ContentVersion();
        if (_index != null && _indexIsLibrary == library && _indexNormalized == normalize && _indexVersion == version) return _index;
        IEnumerable<(OcrBlock, string)> blocks;
        if (library || _doc == null)
        {
            var titles = AppServices.Db.GetDocuments().ToDictionary(d => d.Id, d => d.Title);
            blocks = AppServices.Db.GetAllBlocks().Select(b => (b, titles.TryGetValue(b.DocumentId, out var t) ? t : "？"));
        }
        else
        {
            blocks = AppServices.Db.GetDocumentBlocks(_doc.Document.Id).Select(b => (b, _doc.Title));
        }
        _index = SearchIndex.Build(blocks, normalize);
        _indexIsLibrary = library;
        _indexNormalized = normalize;
        _indexVersion = version;
        return _index;
    }

    private int ContentVersion() => _doc?.ContentVersion ?? 0;

    private async void Search_Click(object sender, RoutedEventArgs e) => await RunSearchAsync();

    private async void KeywordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await RunSearchAsync();
    }

    private void Scope_Changed(object sender, RoutedEventArgs e)
    {
        DocColumn.Visibility = LibraryBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task RunSearchAsync()
    {
        var keyword = KeywordBox.Text.Trim();
        if (keyword.Length == 0) return;
        if (_doc is { IsDirty: true }) _doc.SaveChanges();
        bool library = LibraryBox.IsChecked == true;
        bool normalize = NormalizeBox.IsChecked == true;
        bool regex = RegexBox.IsChecked == true;
        StatsText.Text = "正在检索…";
        Cursor = Cursors.Wait;
        try
        {
            var result = await Task.Run(() => GetIndex(library, normalize).Search(keyword, regex));
            _result = result;
            Grid.ItemsSource = result.Hits;
            if (result.Error != null)
            {
                StatsText.Text = result.Error;
                return;
            }
            StatsText.Text = $"结果：{result.Hits.Count} 条　命中：{result.TotalMatches} 次　页数：{result.PageCount} 页　关键词：「{keyword}」" +
                             (normalize ? "（简繁互通）" : string.Empty) + (regex ? "（正则）" : string.Empty);
        }
        catch (Exception ex)
        {
            StatsText.Text = "检索失败：" + ex.Message;
            AppServices.Log.Error("检索失败", ex);
        }
        finally
        {
            Cursor = Cursors.Arrow;
        }
    }

    private void Jump()
    {
        if (Grid.SelectedItem is not SearchHit hit) return;
        _owner.JumpTo(hit.DocumentId, hit.PageIndex, hit.BlockId, KeywordBox.Text.Trim());
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Jump();
    private void Jump_Click(object sender, RoutedEventArgs e) => Jump();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_result == null || _result.Hits.Count == 0)
        {
            StatsText.Text = "没有可导出的结果";
            return;
        }
        var dlg = new SaveFileDialog
        {
            Title = "导出搜索结果",
            FileName = $"搜索_{_result.Keyword}",
            Filter = "CSV 文件 (*.csv)|*.csv|文本文件 (*.txt)|*.txt",
            DefaultExt = ".csv"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            Exporter.WriteText(dlg.FileName, dlg.FilterIndex == 1 ? Exporter.BuildSearchCsv(_result) : Exporter.BuildSearchText(_result));
            StatsText.Text = "已导出：" + dlg.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message, "导出", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        AppServices.Jobs.PageCompleted -= OnPageCompleted;
        base.OnClosed(e);
    }
}
