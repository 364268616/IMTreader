using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using IMTReader.App.Dialogs;
using IMTReader.App.ViewModels;
using IMTReader.Core.Imaging;
using IMTReader.Core.Models;

namespace IMTReader.App.Views;

public partial class MainWindow : Window
{
    private readonly Dictionary<TabViewModel, FrameworkElement> _views = new();
    private readonly LibraryViewModel _library = new();
    private EraToolWindow? _eraWindow;
    private bool _syncingNav;

    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();
        ViewModel = new MainViewModel();
        DataContext = ViewModel;
        ViewModel.TabOpened += OnTabOpened;
        ViewModel.TabClosed += OnTabClosed;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTab)) UpdateVisibleView();
        };
        foreach (var tab in ViewModel.Tabs) OnTabOpened(tab);
        UpdateVisibleView();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Closing += MainWindow_Closing;
    }

    public LibraryViewModel Library => _library;

    // ------------------------------------------------------------------ 页签管理

    private void OnTabOpened(TabViewModel tab)
    {
        if (_views.ContainsKey(tab)) return;
        FrameworkElement view = tab switch
        {
            DocumentTabViewModel d => new DocumentView(d.Document, this),
            SectionTabViewModel s => s.Kind switch
            {
                SectionKind.Library => new LibraryView(_library, this),
                SectionKind.Tasks => new TaskCenterView(),
                SectionKind.History => new HistoryView(_library, this),
                SectionKind.Settings => new SettingsView(),
                SectionKind.About => new AboutView(),
                _ => new LogView()
            },
            _ => new Grid()
        };
        view.Visibility = Visibility.Collapsed;
        _views[tab] = view;
        ContentHost.Children.Add(view);
    }

    private void OnTabClosed(TabViewModel tab)
    {
        if (_views.Remove(tab, out var view))
        {
            ContentHost.Children.Remove(view);
            (view as IDisposable)?.Dispose();
        }
    }

    private void UpdateVisibleView()
    {
        foreach (var (tab, view) in _views)
        {
            bool visible = tab == ViewModel.SelectedTab;
            view.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible && view is IActivatableView a) a.OnActivated();
        }
        UpdateNavSelection();
    }

    /// <summary>导航栏高亮跟随当前页；打开文献时不高亮任何导航项。</summary>
    private void UpdateNavSelection()
    {
        var current = ViewModel.CurrentSection;
        _syncingNav = true;
        try
        {
            NavLibrary.IsChecked = current == SectionKind.Library;
            NavTasks.IsChecked = current == SectionKind.Tasks;
            NavHistory.IsChecked = current == SectionKind.History;
            NavLog.IsChecked = current == SectionKind.Log;
            NavSettings.IsChecked = current == SectionKind.Settings;
            NavAbout.IsChecked = current == SectionKind.About;
        }
        finally
        {
            _syncingNav = false;
        }
    }

    private void TabHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { DataContext: TabViewModel tab })
        {
            ViewModel.SelectedTab = tab;
            ((ToggleButton)sender).IsChecked = true;
        }
    }

    private void TabClose_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: TabViewModel tab })
        {
            ViewModel.CloseTab(tab);
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ 侧栏

    /// <summary>导航项为一组单选按钮，Tag 即目标页面。程序回填选中态时用 _syncingNav 抑制回调。</summary>
    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingNav) return;
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse<SectionKind>(tag, out var kind))
            ViewModel.ShowSection(kind);
    }

    public void ShowEraTool() => NavEra_Click(this, new RoutedEventArgs());

    private void NavEra_Click(object sender, RoutedEventArgs e)
    {
        if (_eraWindow == null || !_eraWindow.IsLoaded)
        {
            _eraWindow = new EraToolWindow { Owner = this };
            _eraWindow.Show();
        }
        else _eraWindow.Activate();
    }

    private void ActiveJob_Click(object sender, MouseButtonEventArgs e) => ViewModel.ShowSection(SectionKind.Tasks);

    // ------------------------------------------------------------------ 打开/跳转

    public DocumentViewModel OpenDocument(DocumentInfo doc, int? page = null)
    {
        var vm = ViewModel.OpenDocument(doc);
        if (page != null) _ = vm.GoToPageAsync(page.Value);
        return vm;
    }

    /// <summary>跳转到某文献的某页某块（检索结果等）。</summary>
    public async void JumpTo(long documentId, int pageIndex, long? blockId, string? keyword)
    {
        var vm = ViewModel.FindOpenDocument(documentId);
        if (vm == null)
        {
            var doc = AppServices.Db.GetDocument(documentId);
            if (doc == null) return;
            vm = ViewModel.OpenDocument(doc);
        }
        else
        {
            var tab = ViewModel.Tabs.OfType<DocumentTabViewModel>().First(t => t.Document == vm);
            ViewModel.SelectedTab = tab;
        }
        await vm.GoToPageAsync(pageIndex, blockId);
        vm.RequestJump(pageIndex, blockId, keyword);
    }

    public void ImportFromCommandLine(string[] args)
    {
        foreach (var a in args)
        {
            try
            {
                if (File.Exists(a) && BitmapUtil.IsPdf(a)) OpenDocument(_library.ImportPdf(a));
                else if (Directory.Exists(a)) OpenDocument(_library.ImportFolder(a));
            }
            catch (Exception ex)
            {
                AppServices.Log.Error("命令行导入失败：" + a, ex);
            }
        }
    }

    // ------------------------------------------------------------------ 拖放

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        try
        {
            var images = files.Where(f => File.Exists(f) && BitmapUtil.IsSupportedImage(f)).ToList();
            foreach (var f in files)
            {
                if (File.Exists(f) && BitmapUtil.IsPdf(f)) OpenDocument(_library.ImportPdf(f));
                else if (Directory.Exists(f)) OpenDocument(_library.ImportFolder(f));
            }
            if (images.Count > 0) OpenDocument(_library.ImportImages(images));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "导入失败：" + ex.Message, "导入", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ------------------------------------------------------------------ 快捷键

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel.SelectedTab is not DocumentTabViewModel dt || !_views.TryGetValue(dt, out var v) || v is not DocumentView view) return;
        if (view.HandleShortcut(e.Key, Keyboard.Modifiers)) e.Handled = true;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        foreach (var d in ViewModel.Tabs.OfType<DocumentTabViewModel>())
        {
            if (d.Document.IsDirty) d.Document.SaveChanges();
        }
        if (AppServices.Jobs.IsBusy)
        {
            var r = MessageBox.Show(this, "仍有识别任务在进行，退出将中止任务。确定退出？", "退出", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) e.Cancel = true;
        }
    }
}

/// <summary>页签视图激活时的回调（刷新列表等）。</summary>
public interface IActivatableView
{
    void OnActivated();
}
