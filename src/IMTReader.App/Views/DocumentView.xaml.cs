using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using IMTReader.App.Controls;
using IMTReader.App.Dialogs;
using IMTReader.App.ViewModels;
using IMTReader.Core.Export;
using IMTReader.Core.Models;
using IMTReader.Core.Text;
using Microsoft.Win32;

namespace IMTReader.App.Views;

public partial class DocumentView : UserControl, IActivatableView, IDisposable
{
    private readonly DocumentViewModel _vm;
    private readonly MainWindow _owner;
    private BlockEditor? _activeEditor;
    private SearchWindow? _search;
    private HighlightListWindow? _highlightList;
    private NotesWindow? _notes;
    private AiAssistantWindow? _ai;
    private ImmersiveReadingWindow? _immersive;
    private bool _initialized;
    private bool _syncingThumb;
    private bool _fitDone;

    public DocumentView(DocumentViewModel vm, MainWindow owner)
    {
        InitializeComponent();
        _vm = vm;
        _owner = owner;
        DataContext = vm;

        Viewer.BlockClicked += OnBlockClicked;
        Viewer.RegionSelected += OnRegionSelected;
        vm.JumpRequested += OnJumpRequested;
        vm.SelectedBlockChanged += OnSelectedBlockChanged;
        vm.PageImageChanged += OnPageImageChanged;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DocumentViewModel.CurrentPage)) SyncThumbSelection();
        };
        Loaded += async (_, _) =>
        {
            if (_initialized) return;
            _initialized = true;
            await vm.GoToPageAsync(vm.CurrentPage);
            SyncThumbSelection();
        };
    }

    public DocumentViewModel ViewModel => _vm;

    public void OnActivated()
    {
    }

    // ------------------------------------------------------------------ 同步

    private void SyncThumbSelection()
    {
        if (_syncingThumb) return;
        _syncingThumb = true;
        try
        {
            ThumbList.SelectedIndex = _vm.CurrentPage;
            if (ThumbList.SelectedItem != null) ThumbList.ScrollIntoView(ThumbList.SelectedItem);
        }
        finally
        {
            _syncingThumb = false;
        }
    }

    private void OnPageImageChanged()
    {
        if (_fitDone) return;
        _fitDone = true;
        Dispatcher.BeginInvoke(new Action(() => Viewer.FitWidth()), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnBlockClicked(OcrBlock block)
    {
        var vm = _vm.Blocks.FirstOrDefault(b => b.Id == block.Id);
        if (vm == null) return;
        _vm.SelectedBlock = vm;
        ScrollBlockIntoView(vm, focus: true);
    }

    private void OnRegionSelected(Rect region)
    {
        _vm.RecognizeRegion(region, rubbing: false);
    }

    private void OnSelectedBlockChanged(BlockViewModel? block)
    {
        if (block != null) Viewer.ScrollToBlock(block.Block);
    }

    private void OnJumpRequested(int pageIndex, long? blockId, string? keyword)
    {
        var vm = blockId != null ? _vm.Blocks.FirstOrDefault(b => b.Id == blockId.Value) : null;
        if (vm == null) return;
        _vm.SelectedBlock = vm;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ScrollBlockIntoView(vm, focus: true);
            if (!string.IsNullOrWhiteSpace(keyword) && vm.Editor != null)
            {
                var ranges = SearchIndex.FindRanges(vm.DisplayText, keyword, AppServices.Settings.Current.SearchIgnoreVariants, false);
                if (ranges.Count > 0) vm.Editor.SelectRange(ranges[0].Start, ranges[0].Length);
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ScrollBlockIntoView(BlockViewModel vm, bool focus)
    {
        BlocksItems.UpdateLayout();
        if (BlocksItems.ItemContainerGenerator.ContainerFromItem(vm) is FrameworkElement container)
        {
            container.BringIntoView();
            if (focus && vm.Editor != null)
            {
                _activeEditor = vm.Editor;
                if (!vm.Editor.TextBox.IsKeyboardFocusWithin) vm.Editor.FocusEditor();
            }
        }
    }

    private BlockViewModel? ActiveBlock => _activeEditor?.ViewModel ?? _vm.SelectedBlock;

    // ------------------------------------------------------------------ 导航/缩放

    private async void Prev_Click(object sender, RoutedEventArgs e) => await _vm.PrevPageAsync();
    private async void Next_Click(object sender, RoutedEventArgs e) => await _vm.NextPageAsync();

    private async void PageBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (int.TryParse(PageBox.Text.Trim(), out int n)) await _vm.GoToPageAsync(n - 1);
        else PageBox.Text = _vm.PageNumber.ToString();
        Viewer.Focus();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => _vm.Zoom *= 1.2;
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => _vm.Zoom /= 1.2;
    private void FitWidth_Click(object sender, RoutedEventArgs e) => Viewer.FitWidth();
    private void FitPage_Click(object sender, RoutedEventArgs e) => Viewer.FitPage();
    private void RotateLeft_Click(object sender, RoutedEventArgs e) => _vm.Rotation -= 90;
    private void RotateRight_Click(object sender, RoutedEventArgs e) => _vm.Rotation += 90;
    private void FontSmaller_Click(object sender, RoutedEventArgs e) => _vm.FontSize -= 1;
    private void FontLarger_Click(object sender, RoutedEventArgs e) => _vm.FontSize += 1;

    private async void ThumbList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingThumb || ThumbList.SelectedIndex < 0) return;
        await _vm.GoToPageAsync(ThumbList.SelectedIndex);
    }

    private void Thumb_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PageThumbViewModel thumb }) _ = _vm.EnsureThumbnailAsync(thumb);
    }

    // ------------------------------------------------------------------ 文本操作

    private void Editor_Activated(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not BlockEditor editor) return;
        _activeEditor = editor;
        if (editor.ViewModel != null && _vm.SelectedBlock != editor.ViewModel) _vm.SelectedBlock = editor.ViewModel;
    }

    private void Editor_ContentEdited(object sender, RoutedEventArgs e) => _vm.IsDirty = true;

    private void Editor_ActionRequested(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not BlockEditor editor) return;
        _activeEditor = editor;
        switch (editor.LastAction)
        {
            case "highlight":
                Highlight_Click(sender, e);
                break;
            case "dictionary":
                LookupDictionary(editor.SelectedText);
                break;
            case "speak":
                Speak(editor.SelectedText);
                break;
            case "copy-citation":
                CopyWithCitation(editor);
                break;
            case "ai":
                OpenAi(editor.HasSelection ? editor.SelectedText : editor.ViewModel?.DisplayText);
                break;
            case "menu":
                ShowBlockMenu(editor);
                break;
        }
    }

    private void ShowBlockMenu(BlockEditor editor)
    {
        var vm = editor.ViewModel;
        if (vm == null) return;
        var menu = new ContextMenu();
        menu.Items.Add(MakeItem("定位原图", () => Viewer.ScrollToBlock(vm.Block)));
        menu.Items.Add(MakeItem("复制本块文本", () => SafeSetClipboard(vm.DisplayText)));
        menu.Items.Add(MakeItem("复制本块并附带出处", () => SafeSetClipboard(vm.DisplayText + Environment.NewLine + "——" + _vm.Citation(vm.Order))));
        menu.Items.Add(MakeItem("朗读本块", () => Speak(vm.DisplayText)));
        menu.Items.Add(MakeItem("AI 助手（本块）", () => OpenAi(vm.DisplayText)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem("删除此块", () =>
        {
            if (MessageBox.Show(_owner, $"删除文本块 {vm.Label}？", "删除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                _vm.DeleteBlock(vm);
        }));
        menu.PlacementTarget = editor;
        menu.IsOpen = true;
    }

    private static MenuItem MakeItem(string header, Action action, bool? isChecked = null, bool enabled = true)
    {
        var mi = new MenuItem { Header = header, IsEnabled = enabled };
        if (isChecked.HasValue)
        {
            mi.IsCheckable = true;
            mi.IsChecked = isChecked.Value;
        }
        mi.Click += (_, _) => action();
        return mi;
    }

    private static MenuItem MakeSubMenu(string header, params object[] items)
    {
        var mi = new MenuItem { Header = header };
        foreach (var item in items) mi.Items.Add(item);
        return mi;
    }

    private ContextMenu NewMenu()
    {
        return new ContextMenu
        {
            PlacementTarget = MoreButton,
            Placement = PlacementMode.Bottom,
            HorizontalOffset = -60
        };
    }

    private void Highlight_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.IsEditable)
        {
            _vm.StatusText = "简体显示模式下不可编辑高亮，请先切回原文";
            return;
        }
        var editor = _activeEditor;
        if (editor == null || !editor.HasSelection)
        {
            _vm.StatusText = "请先在右侧文本中选中要高亮的文字";
            return;
        }
        if (editor.HighlightSelection(AppServices.Settings.Current.DefaultHighlightColor))
        {
            _vm.IsDirty = true;
            _vm.SaveChanges();
        }
    }

    private void ClearHighlights_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(_owner, "清除本页所有高亮？", "清除高亮", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _vm.ClearPageHighlights();
    }

    private void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        var editor = _activeEditor;
        string text = editor is { HasSelection: true } ? editor.SelectedText : ActiveBlock?.DisplayText ?? string.Empty;
        if (text.Length == 0)
        {
            _vm.StatusText = "没有可复制的文字";
            return;
        }
        SafeSetClipboard(text);
        _vm.StatusText = $"已复制 {text.Length} 字";
    }

    private void CopyWithCitation(BlockEditor editor)
    {
        var text = editor.HasSelection ? editor.SelectedText : editor.ViewModel?.DisplayText ?? string.Empty;
        SafeSetClipboard(text + Environment.NewLine + "——" + _vm.Citation(editor.ViewModel?.Order));
        _vm.StatusText = "已复制（附出处）";
    }

    private static void SafeSetClipboard(string text)
    {
        try { Clipboard.SetText(text); } catch { }
    }

    private void LookupDictionary(string? selected)
    {
        var term = (selected ?? string.Empty).Trim();
        if (term.Length == 0)
        {
            _vm.StatusText = "请先选中要查询的字或词";
            return;
        }
        if (term.Length > 8) term = term[..8];
        var url = string.Format(AppServices.Settings.Current.DictionaryUrlTemplate, Uri.EscapeDataString(term));
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { _vm.StatusText = "打开字典失败：" + ex.Message; }
    }

    private void Speak(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            AppServices.Tts.Stop();
            return;
        }
        try { AppServices.Tts.Speak(text); }
        catch (Exception ex) { _vm.StatusText = "朗读失败：" + ex.Message; }
    }

    private void Speak_Click(object sender, RoutedEventArgs e)
    {
        if (AppServices.Tts.IsSpeaking)
        {
            AppServices.Tts.Stop();
            _vm.StatusText = "已停止朗读";
            return;
        }
        var editor = _activeEditor;
        var text = editor is { HasSelection: true } ? editor.SelectedText : ActiveBlock?.DisplayText ?? _vm.CurrentPageText(_vm.ShowSimplified);
        Speak(text);
        _vm.StatusText = "正在朗读（再次点击停止）";
    }

    private void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_search == null || !_search.IsLoaded)
        {
            _search = new SearchWindow(_owner, _vm) { Owner = _owner };
            _search.Show();
        }
        else _search.Activate();
        _search.FocusInput();
    }

    private void HighlightList_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsDirty) _vm.SaveChanges();
        if (_highlightList == null || !_highlightList.IsLoaded)
        {
            _highlightList = new HighlightListWindow(_owner, _vm) { Owner = _owner };
            _highlightList.Show();
        }
        else
        {
            _highlightList.Reload();
            _highlightList.Activate();
        }
    }

    private void Notes_Click(object sender, RoutedEventArgs e) => OpenNotes();

    private NotesWindow OpenNotes()
    {
        if (_notes == null || !_notes.IsLoaded)
        {
            _notes = new NotesWindow(_vm) { Owner = _owner };
            _notes.Show();
        }
        else _notes.Activate();
        return _notes;
    }

    private void Bookmark_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsCurrentPageBookmarked)
        {
            _vm.ToggleBookmark();
            return;
        }
        var name = InputDialog.Show(_owner, "添加书签", "书签名称：", $"第 {_vm.PageNumber} 页");
        if (name == null) return;
        _vm.ToggleBookmark(name);
    }

    private void Ai_Click(object sender, RoutedEventArgs e)
    {
        var editor = _activeEditor;
        OpenAi(editor is { HasSelection: true } ? editor.SelectedText : ActiveBlock?.DisplayText);
    }

    private void OpenAi(string? text)
    {
        if (!AppServices.Ai.IsConfigured)
        {
            if (MessageBox.Show(_owner, "尚未配置 AI 服务。是否现在前往设置？", "AI 助手", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                _owner.ViewModel.ShowSection(SectionKind.Settings);
            return;
        }
        var block = ActiveBlock;
        if (_ai == null || !_ai.IsLoaded)
        {
            _ai = new AiAssistantWindow(_vm, AppendToNotes) { Owner = _owner };
            _ai.Show();
        }
        _ai.SetSource(text ?? block?.DisplayText ?? string.Empty, block);
        _ai.Activate();
    }

    private void AppendToNotes(string text)
    {
        var notes = OpenNotes();
        notes.Append(text);
    }

    private void Immersive_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsDirty) _vm.SaveChanges();
        if (_immersive == null || !_immersive.IsLoaded)
        {
            _immersive = new ImmersiveReadingWindow(_vm) { Owner = _owner };
            _immersive.Show();
        }
        else
        {
            _immersive.ShowPage(_vm.CurrentPage);
            _immersive.Activate();
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "导出全文",
            FileName = _vm.Title,
            Filter = "文本文件 (*.txt)|*.txt|Markdown (*.md)|*.md",
            DefaultExt = ".txt"
        };
        if (dlg.ShowDialog(_owner) != true) return;
        bool markdown = dlg.FilterIndex == 2;
        var join = MessageBox.Show(_owner, "是否把每个文本块内的各栏/各行合并为一段（竖排转横排后连排）？\n选“否”则保留每栏一行。", "导出全文", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (join == MessageBoxResult.Cancel) return;
        try
        {
            var text = _vm.BuildFullText(markdown, pageMarkers: true, simplified: _vm.ShowSimplified, joinLines: join == MessageBoxResult.Yes);
            Exporter.WriteText(dlg.FileName, text);
            _vm.StatusText = "已导出：" + dlg.FileName;
            AppServices.Log.Info("导出全文：" + dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(_owner, "导出失败：" + ex.Message, "导出", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e) => _vm.SaveChanges();

    private void Ocr_Click(object sender, RoutedEventArgs e)
    {
        var menu = NewMenu();
        menu.PlacementTarget = OcrButton;
        menu.HorizontalOffset = 0;
        menu.Items.Add(MakeItem("识别当前页（F5）", () => RecognizePage(false)));
        menu.Items.Add(MakeItem("识别全文", () => RecognizeAll(false)));
        menu.Items.Add(MakeItem("识别指定页码范围…", RecognizeRange));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem("拓片：识别当前页", () => RecognizePage(true)));
        menu.Items.Add(MakeItem("拓片：识别全文", () => RecognizeAll(true)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeSubMenu("识别引擎",
            MakeItem("PaddleOCR 本地（推荐）", () => _vm.EngineKind = OcrEngineKind.Paddle, _vm.EngineKind == OcrEngineKind.Paddle),
            MakeItem("Windows 内置 OCR（仅横排）", () => _vm.EngineKind = OcrEngineKind.WindowsBuiltIn, _vm.EngineKind == OcrEngineKind.WindowsBuiltIn),
            MakeItem("AI 视觉模型（需配置）", () => _vm.EngineKind = OcrEngineKind.VisionLlm, _vm.EngineKind == OcrEngineKind.VisionLlm)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem("删除本页识别结果", () => DeletePageOcr_Click(this, new RoutedEventArgs()), null, _vm.CurrentPageHasOcr));
        menu.IsOpen = true;
    }

    private void RecognizeRange()
    {
        var input = InputDialog.Show(_owner, "范围识别", $"要识别的页码范围（共 {_vm.PageCount} 页，例如 1-{_vm.PageCount}）：", $"1-{_vm.PageCount}");
        if (string.IsNullOrWhiteSpace(input)) return;
        var parts = input.Replace('－', '-').Replace('—', '-')
            .Split(new[] { '-', ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0].Trim(), out int from))
        {
            _vm.StatusText = "页码范围无效，请按“起始页-结束页”填写";
            return;
        }
        int to = parts.Length > 1 && int.TryParse(parts[1].Trim(), out int parsed) ? parsed : from;
        _vm.RecognizeRange(from, to, rubbing: false);
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        var menu = NewMenu();
        menu.Items.Add(MakeItem(_vm.IsCurrentPageBookmarked ? "删除本页书签（Ctrl+B）" : "添加本页书签（Ctrl+B）", () => Bookmark_Click(this, new RoutedEventArgs())));
        menu.Items.Add(MakeItem("书签列表…", ShowBookmarks));
        menu.Items.Add(MakeItem("高亮列表…", () => HighlightList_Click(this, new RoutedEventArgs())));
        menu.Items.Add(MakeItem("清除本页高亮", () => ClearHighlights_Click(this, new RoutedEventArgs())));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem("复制选中文字", () => CopySelected_Click(this, new RoutedEventArgs())));
        menu.Items.Add(MakeItem("复制选中并附带出处", () => { if (_activeEditor != null) CopyWithCitation(_activeEditor); }));
        menu.Items.Add(MakeItem("查字典（选中文字）", () => LookupDictionary(_activeEditor?.SelectedText)));
        menu.Items.Add(MakeItem("朗读 / 停止朗读", () => Speak_Click(this, new RoutedEventArgs())));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem("AI 助手（标点 / 翻译 / 摘要 / 实体 / 注释）…", () => Ai_Click(this, new RoutedEventArgs())));
        menu.Items.Add(MakeItem("沉浸阅读（竖排仿古书版式）…", () => Immersive_Click(this, new RoutedEventArgs())));
        menu.Items.Add(MakeItem("纪年换算…", _owner.ShowEraTool));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeSubMenu("图像显示",
            MakeItem("原图", () => _vm.EnhanceMode = ImageEnhanceMode.None, _vm.EnhanceMode == ImageEnhanceMode.None),
            MakeItem("反白（拓片）", () => _vm.EnhanceMode = ImageEnhanceMode.Invert, _vm.EnhanceMode == ImageEnhanceMode.Invert),
            MakeItem("二值化", () => _vm.EnhanceMode = ImageEnhanceMode.Binarize, _vm.EnhanceMode == ImageEnhanceMode.Binarize),
            MakeItem("灰度", () => _vm.EnhanceMode = ImageEnhanceMode.Grayscale, _vm.EnhanceMode == ImageEnhanceMode.Grayscale),
            MakeItem("高对比", () => _vm.EnhanceMode = ImageEnhanceMode.HighContrast, _vm.EnhanceMode == ImageEnhanceMode.HighContrast)));
        menu.Items.Add(MakeItem("在原图上显示文本块框", () => _vm.ShowBoxes = !_vm.ShowBoxes, _vm.ShowBoxes));
        menu.Items.Add(MakeItem("适合页面", () => Viewer.FitPage()));
        menu.Items.Add(MakeItem("复制当前页图片", CopyPageImage));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem("导出全文…（Ctrl+E）", () => Export_Click(this, new RoutedEventArgs())));
        menu.IsOpen = true;
    }

    private void ShowBookmarks()
    {
        var bookmarks = _vm.GetBookmarks();
        if (bookmarks.Count == 0)
        {
            _vm.StatusText = "本文献尚无书签";
            return;
        }
        var menu = NewMenu();
        foreach (var b in bookmarks)
        {
            var page = b.PageIndex;
            menu.Items.Add(MakeItem($"第 {page + 1} 页　{b.Title}", () => _ = _vm.GoToPageAsync(page)));
        }
        menu.IsOpen = true;
    }

    private void CopyPageImage()
    {
        var img = _vm.RawPageImage;
        if (img == null) return;
        try
        {
            Clipboard.SetImage(img);
            _vm.StatusText = "已复制当前页图片";
        }
        catch (Exception ex)
        {
            _vm.StatusText = "复制失败：" + ex.Message;
        }
    }

    // ------------------------------------------------------------------ OCR

    private void RecognizePage(bool rubbing)
    {
        if (_vm.CurrentPageHasOcr &&
            MessageBox.Show(_owner, "本页已有识别结果，重新识别将覆盖文本与高亮。继续？", "识别", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        _vm.RecognizeCurrentPage(rubbing);
    }

    private void RecognizeAll(bool rubbing)
    {
        bool skipDone = true;
        if (_vm.Document.OcrDonePages > 0)
        {
            var r = MessageBox.Show(_owner, $"已有 {_vm.Document.OcrDonePages} 页识别完成。\n“是”：跳过已识别页；“否”：全部重新识别。", "全文识别", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return;
            skipDone = r == MessageBoxResult.Yes;
        }
        _vm.RecognizeAllPages(rubbing, skipDone);
    }

    private void DeletePageOcr_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(_owner, "删除本页的识别文本与高亮？", "删除", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _vm.DeletePageResults();
    }

    // ------------------------------------------------------------------ 快捷键

    public bool HandleShortcut(Key key, ModifierKeys modifiers)
    {
        bool ctrl = (modifiers & ModifierKeys.Control) != 0;
        if (ctrl)
        {
            switch (key)
            {
                case Key.F: Search_Click(this, new RoutedEventArgs()); return true;
                case Key.S: _vm.SaveChanges(); return true;
                case Key.H: Highlight_Click(this, new RoutedEventArgs()); return true;
                case Key.B: Bookmark_Click(this, new RoutedEventArgs()); return true;
                case Key.N: Notes_Click(this, new RoutedEventArgs()); return true;
                case Key.E: Export_Click(this, new RoutedEventArgs()); return true;
                case Key.OemPlus or Key.Add: _vm.Zoom *= 1.2; return true;
                case Key.OemMinus or Key.Subtract: _vm.Zoom /= 1.2; return true;
                case Key.D0 or Key.NumPad0: Viewer.FitWidth(); return true;
                case Key.PageDown or Key.Right: _ = _vm.NextPageAsync(); return true;
                case Key.PageUp or Key.Left: _ = _vm.PrevPageAsync(); return true;
            }
        }
        else
        {
            switch (key)
            {
                case Key.F5: RecognizePage(false); return true;
                case Key.Escape:
                    if (_vm.IsSelectMode)
                    {
                        _vm.IsSelectMode = false;
                        return true;
                    }
                    break;
                case Key.PageDown when !IsTextInputFocused(): _ = _vm.NextPageAsync(); return true;
                case Key.PageUp when !IsTextInputFocused(): _ = _vm.PrevPageAsync(); return true;
            }
        }
        return false;
    }

    private static bool IsTextInputFocused() => Keyboard.FocusedElement is TextBoxBase;

    public void Dispose()
    {
        _search?.Close();
        _highlightList?.Close();
        _notes?.Close();
        _ai?.Close();
        _immersive?.Close();
        Viewer.BlockClicked -= OnBlockClicked;
        Viewer.RegionSelected -= OnRegionSelected;
        _vm.JumpRequested -= OnJumpRequested;
        _vm.SelectedBlockChanged -= OnSelectedBlockChanged;
        _vm.PageImageChanged -= OnPageImageChanged;
    }
}
