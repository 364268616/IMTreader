using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IMTReader.App.Dialogs;
using IMTReader.App.ViewModels;
using IMTReader.Core.Models;
using IMTReader.Core.Ocr;
using Microsoft.Win32;

namespace IMTReader.App.Views;

public partial class LibraryView : UserControl, IActivatableView
{
    private readonly LibraryViewModel _vm;
    private readonly MainWindow _owner;

    public LibraryView(LibraryViewModel vm, MainWindow owner)
    {
        InitializeComponent();
        _vm = vm;
        _owner = owner;
        DataContext = vm;
        vm.Reload();
    }

    public void OnActivated() => _vm.Reload();

    private DocumentItemViewModel? Selected => Grid.SelectedItem as DocumentItemViewModel;

    private void Open(DocumentItemViewModel? item)
    {
        if (item == null) return;
        if (!item.Document.SourceExists())
        {
            MessageBox.Show(_owner, "源文件不存在，请通过右键菜单“重新定位源文件”。", "打开", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        try
        {
            _owner.OpenDocument(item.Document);
        }
        catch (Exception ex)
        {
            AppServices.Log.Error("打开文献失败", ex);
            MessageBox.Show(_owner, "打开失败：" + ex.Message, "打开", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Open(Selected);
    private void Open_Click(object sender, RoutedEventArgs e) => Open(Selected);

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e) => _vm.Filter = FilterBox.Text;

    private void ImportPdf_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "PDF 文件 (*.pdf)|*.pdf", Multiselect = true, Title = "导入 PDF" };
        if (dlg.ShowDialog(_owner) != true) return;
        DocumentInfo? last = null;
        foreach (var f in dlg.FileNames)
        {
            try { last = _vm.ImportPdf(f); }
            catch (Exception ex)
            {
                AppServices.Log.Error("导入 PDF 失败：" + f, ex);
                MessageBox.Show(_owner, $"导入失败：{f}\n{ex.Message}", "导入", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        if (last != null && dlg.FileNames.Length == 1) _owner.OpenDocument(last);
    }

    private void ImportImages_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.gif;*.webp|所有文件|*.*",
            Multiselect = true,
            Title = "导入图片（可多选，将按文件名自然排序作为页序）"
        };
        if (dlg.ShowDialog(_owner) != true || dlg.FileNames.Length == 0) return;
        try
        {
            var doc = _vm.ImportImages(dlg.FileNames);
            _owner.OpenDocument(doc);
        }
        catch (Exception ex)
        {
            AppServices.Log.Error("导入图片失败", ex);
            MessageBox.Show(_owner, "导入失败：" + ex.Message, "导入", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "选择图片文件夹（文件按自然顺序排序作为页序）" };
        if (dlg.ShowDialog(_owner) != true) return;
        try
        {
            _owner.OpenDocument(_vm.ImportFolder(dlg.FolderName));
        }
        catch (Exception ex)
        {
            AppServices.Log.Error("导入文件夹失败", ex);
            MessageBox.Show(_owner, "导入失败：" + ex.Message, "导入", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OcrAll_Click(object sender, RoutedEventArgs e) => EnqueueAll(false);
    private void OcrAllRubbing_Click(object sender, RoutedEventArgs e) => EnqueueAll(true);

    private void EnqueueAll(bool rubbing)
    {
        var item = Selected;
        if (item == null) return;
        var pages = Enumerable.Range(0, item.PageCount).ToArray();
        var opts = new OcrOptions { Rubbing = rubbing, Binarize = rubbing && AppServices.Settings.Current.RubbingBinarize };
        AppServices.Jobs.Enqueue(item.Document, pages, opts, (rubbing ? "全文拓片识别" : "全文识别") + $"（{pages.Length} 页）");
        _owner.ViewModel.ShowSection(SectionKind.Tasks);
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        var item = Selected;
        if (item == null) return;
        var name = InputDialog.Show(_owner, "重命名", "新的标题：", item.Title);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _vm.Rename(item, name);
            var open = _owner.ViewModel.FindOpenDocument(item.Id);
            if (open != null) MessageBox.Show(_owner, "已重命名，重新打开后页签标题更新。", "重命名", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Relocate_Click(object sender, RoutedEventArgs e)
    {
        var item = Selected;
        if (item == null) return;
        string? newPath = null;
        if (item.Document.Kind == SourceKind.Pdf)
        {
            var dlg = new OpenFileDialog { Filter = "PDF 文件 (*.pdf)|*.pdf", Title = "重新定位 PDF" };
            if (dlg.ShowDialog(_owner) == true) newPath = dlg.FileName;
        }
        else if (item.Document.Kind == SourceKind.ImageFolder)
        {
            var dlg = new OpenFolderDialog { Title = "重新定位图片文件夹" };
            if (dlg.ShowDialog(_owner) == true) newPath = dlg.FolderName;
        }
        else
        {
            var dlg = new OpenFileDialog { Filter = "图片文件|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp;*.gif;*.webp", Multiselect = true, Title = "重新选择图片文件" };
            if (dlg.ShowDialog(_owner) == true) newPath = string.Join(DocumentInfo.PathSeparator, dlg.FileNames);
        }
        if (newPath != null) _vm.Relocate(item, newPath);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var item = Selected;
        if (item == null) return;
        var path = item.Document.Kind == SourceKind.ImageFiles ? item.Document.GetImageFiles().FirstOrDefault() : item.Document.SourcePath;
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", $"/select,\"{path}\"");
            else if (Directory.Exists(path)) Process.Start("explorer.exe", $"\"{path}\"");
        }
        catch (Exception ex)
        {
            AppServices.Log.Warn("打开文件夹失败：" + ex.Message);
        }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var item = Selected;
        if (item == null) return;
        var r = MessageBox.Show(_owner, $"从文献库移除《{item.Title}》？\n识别文本、高亮与笔记将一并删除，源文件不受影响。", "移除", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        _owner.ViewModel.CloseDocument(item.Id);
        _vm.Delete(item);
    }
}
