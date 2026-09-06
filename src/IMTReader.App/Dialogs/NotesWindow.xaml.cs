using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IMTReader.App.ViewModels;
using IMTReader.Core.Export;
using IMTReader.Core.Text;
using Microsoft.Win32;

namespace IMTReader.App.Dialogs;

public partial class NotesWindow : Window
{
    private readonly DocumentViewModel _doc;
    private bool _dirty;
    private bool _loading;

    public NotesWindow(DocumentViewModel doc)
    {
        InitializeComponent();
        _doc = doc;
        Title = $"笔记 — {doc.Title}";
        Load();
    }

    private void Load()
    {
        _loading = true;
        Editor.Text = AppServices.Db.GetNotes(_doc.Document.Id);
        _loading = false;
        _dirty = false;
        StatusText.Text = Editor.Text.Length == 0 ? "尚无笔记" : $"{Editor.Text.Length} 字";
    }

    public void Append(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (Editor.Text.Length > 0 && !Editor.Text.EndsWith('\n')) Editor.AppendText(Environment.NewLine);
        Editor.AppendText(Environment.NewLine + text + Environment.NewLine);
        Editor.ScrollToEnd();
        Save();
        if (ReadToggle.IsChecked == true) RenderReader();
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _dirty = true;
        StatusText.Text = "未保存";
    }

    private void Save()
    {
        AppServices.Db.SaveNotes(_doc.Document.Id, Editor.Text);
        _dirty = false;
        StatusText.Text = $"已保存 {DateTime.Now:HH:mm:ss}";
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void ReadToggle_Changed(object sender, RoutedEventArgs e)
    {
        bool read = ReadToggle.IsChecked == true;
        if (read) RenderReader();
        Reader.Visibility = read ? Visibility.Visible : Visibility.Collapsed;
        Editor.Visibility = read ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RenderReader()
    {
        var font = (FontFamily)Application.Current.Resources["ReadingFontFamily"];
        var fg = (Brush)Application.Current.Resources["ReadingForegroundBrush"];
        Reader.Document = MarkdownRenderer.Render(Editor.Text, font, 15, fg);
    }

    private void InsertCitation_Click(object sender, RoutedEventArgs e)
    {
        ReadToggle.IsChecked = false;
        InsertAtCaret("（" + _doc.Citation(_doc.SelectedBlock?.Order) + "）");
    }

    private void InsertBlock_Click(object sender, RoutedEventArgs e)
    {
        var block = _doc.SelectedBlock;
        if (block == null)
        {
            StatusText.Text = "请先在阅读界面选中一个文本块";
            return;
        }
        ReadToggle.IsChecked = false;
        InsertAtCaret("> " + block.DisplayText.Replace("\n", "\n> ") + Environment.NewLine + "（" + _doc.Citation(block.Order) + "）" + Environment.NewLine);
    }

    private void InsertAtCaret(string text)
    {
        int pos = Editor.CaretIndex;
        Editor.Text = Editor.Text.Insert(pos, text);
        Editor.CaretIndex = pos + text.Length;
        Editor.Focus();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Title = "导出笔记", FileName = _doc.Title + "_笔记", Filter = "Markdown (*.md)|*.md|文本文件 (*.txt)|*.txt", DefaultExt = ".md" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var content = dlg.FilterIndex == 2 ? MarkdownLite.ToPlainText(Editor.Text) : Editor.Text;
            Exporter.WriteText(dlg.FileName, content);
            StatusText.Text = "已导出：" + dlg.FileName;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导出失败：" + ex.Message, "导出", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_dirty) Save();
        base.OnClosing(e);
    }
}
