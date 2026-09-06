using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using IMTReader.App.ViewModels;
using IMTReader.Core.Models;

namespace IMTReader.App.Controls;

/// <summary>
/// 单个文本块的编辑器：RichTextBox 承载文本，用 Run 的背景色表示高亮。
/// 文本模型：一个 Paragraph，行之间用 LineBreak；Extract() 按同样规则还原文本与高亮偏移。
/// </summary>
public partial class BlockEditor : UserControl
{
    public static readonly RoutedEvent ActivatedEvent = EventManager.RegisterRoutedEvent(
        nameof(Activated), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(BlockEditor));

    public static readonly RoutedEvent ContentEditedEvent = EventManager.RegisterRoutedEvent(
        nameof(ContentEdited), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(BlockEditor));

    public static readonly RoutedEvent ActionRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(ActionRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(BlockEditor));

    private bool _loading;
    private string _lastAction = string.Empty;

    public BlockEditor()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => UpdateSelectionVisual();
    }

    public event RoutedEventHandler Activated
    {
        add => AddHandler(ActivatedEvent, value);
        remove => RemoveHandler(ActivatedEvent, value);
    }

    public event RoutedEventHandler ContentEdited
    {
        add => AddHandler(ContentEditedEvent, value);
        remove => RemoveHandler(ContentEditedEvent, value);
    }

    /// <summary>右键菜单中的扩展操作请求（LastAction 指明动作）。</summary>
    public event RoutedEventHandler ActionRequested
    {
        add => AddHandler(ActionRequestedEvent, value);
        remove => RemoveHandler(ActionRequestedEvent, value);
    }

    public string LastAction => _lastAction;

    public BlockViewModel? ViewModel => DataContext as BlockViewModel;

    public RichTextBox TextBox => Rtb;

    public string SelectedText => Rtb.Selection.Text;

    public bool HasSelection => !Rtb.Selection.IsEmpty;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is BlockViewModel old)
        {
            old.PropertyChanged -= OnViewModelPropertyChanged;
            if (old.Editor == this) old.Editor = null;
        }
        if (e.NewValue is BlockViewModel vm)
        {
            vm.Editor = this;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            Reload();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(BlockViewModel.IsSelected):
                UpdateSelectionVisual();
                break;
            case nameof(BlockViewModel.ShowSimplified):
            case nameof(BlockViewModel.DisplayText):
                Reload();
                break;
        }
    }

    private void UpdateSelectionVisual()
    {
        var vm = ViewModel;
        var key = vm is { IsSelected: true } ? "BlockSelectedBrush" : "BlockBackgroundBrush";
        Root.Background = TryFindResource(key) as Brush
                          ?? (vm is { IsSelected: true } ? Brushes.Honeydew : Brushes.White);
    }

    /// <summary>按视图模型当前内容重新加载文档。</summary>
    public void Reload()
    {
        var vm = ViewModel;
        if (vm == null) return;
        LabelText.Text = vm.Label;
        MetaText.Text = vm.OrientationText + (vm.ScoreText.Length > 0 ? " · 置信度 " + vm.ScoreText : string.Empty);
        Rtb.IsReadOnly = vm.ShowSimplified;
        Rtb.IsReadOnlyCaretVisible = true;
        LoadText(vm.DisplayText, vm.Highlights);
        UpdateSelectionVisual();
    }

    public void LoadText(string text, IEnumerable<Highlight> highlights)
    {
        _loading = true;
        try
        {
            var doc = new FlowDocument { PagePadding = new Thickness(0) };
            var para = new Paragraph();
            var hl = highlights.Where(h => h.Length > 0).OrderBy(h => h.Start).ToList();
            var lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int offset = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    para.Inlines.Add(new LineBreak());
                    offset++;
                }
                AppendLine(para, lines[i], offset, hl);
                offset += lines[i].Length;
            }
            doc.Blocks.Add(para);
            Rtb.Document = doc;
        }
        finally
        {
            _loading = false;
        }
    }

    private static void AppendLine(Paragraph para, string line, int lineStart, List<Highlight> highlights)
    {
        int pos = 0;
        while (pos < line.Length)
        {
            int abs = lineStart + pos;
            var h = highlights.FirstOrDefault(x => x.Start <= abs && abs < x.Start + x.Length);
            if (h != null)
            {
                int end = Math.Min(line.Length, h.Start + h.Length - lineStart);
                para.Inlines.Add(new Run(line[pos..end]) { Background = MakeBrush(h.Color) });
                pos = end;
                continue;
            }
            var next = highlights.Where(x => x.Start > abs).Select(x => x.Start).DefaultIfEmpty(int.MaxValue).Min();
            int plainEnd = Math.Min(line.Length, next - lineStart);
            if (plainEnd <= pos) plainEnd = line.Length;
            para.Inlines.Add(new Run(line[pos..plainEnd]));
            pos = plainEnd;
        }
    }

    private static Brush MakeBrush(string hex)
    {
        try
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
        catch
        {
            return Brushes.Yellow;
        }
    }

    /// <summary>从文档中提取文本与高亮区间。</summary>
    public (string Text, List<(int Start, int Length, string Color)> Highlights) Extract()
    {
        var sb = new System.Text.StringBuilder();
        var highlights = new List<(int Start, int Length, string Color)>();
        bool firstPara = true;
        foreach (var block in Rtb.Document.Blocks)
        {
            if (!firstPara) sb.Append('\n');
            firstPara = false;
            if (block is Paragraph p) Walk(p.Inlines, null, sb, highlights);
        }
        // 合并相邻同色区间
        var merged = new List<(int Start, int Length, string Color)>();
        foreach (var h in highlights.OrderBy(h => h.Start))
        {
            if (merged.Count > 0)
            {
                var last = merged[^1];
                if (last.Start + last.Length == h.Start && last.Color == h.Color)
                {
                    merged[^1] = (last.Start, last.Length + h.Length, last.Color);
                    continue;
                }
            }
            merged.Add(h);
        }
        return (sb.ToString(), merged);
    }

    private static void Walk(InlineCollection inlines, Brush? inherited, System.Text.StringBuilder sb, List<(int, int, string)> highlights)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                {
                    var bg = run.Background ?? inherited;
                    if (bg is SolidColorBrush scb && scb.Color.A > 0 && run.Text.Length > 0)
                        highlights.Add((sb.Length, run.Text.Length, ToHex(scb.Color)));
                    sb.Append(run.Text);
                    break;
                }
                case LineBreak:
                    sb.Append('\n');
                    break;
                case Span span:
                    Walk(span.Inlines, span.Background ?? inherited, sb, highlights);
                    break;
                case InlineUIContainer:
                    break;
            }
        }
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // ------------------------------------------------------------------ 操作

    public bool HighlightSelection(string colorHex)
    {
        if (Rtb.Selection.IsEmpty) return false;
        Rtb.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, MakeBrush(colorHex));
        RaiseEdited();
        return true;
    }

    public void UnhighlightSelection()
    {
        if (Rtb.Selection.IsEmpty) return;
        Rtb.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, null);
        RaiseEdited();
    }

    public void ClearHighlights()
    {
        var range = new TextRange(Rtb.Document.ContentStart, Rtb.Document.ContentEnd);
        range.ApplyPropertyValue(TextElement.BackgroundProperty, null);
    }

    /// <summary>选中（并滚动到）给定字符区间。</summary>
    public void SelectRange(int start, int length)
    {
        var s = PointerAt(start);
        var e = PointerAt(start + length);
        if (s == null || e == null) return;
        Rtb.Selection.Select(s, e);
        Rtb.Focus();
        try
        {
            var rect = s.GetCharacterRect(LogicalDirection.Forward);
            BringIntoView(new Rect(rect.X, rect.Y, rect.Width, rect.Height));
        }
        catch
        {
            BringIntoView();
        }
    }

    /// <summary>临时高亮关键词命中位置（不写入数据库；重新加载即消失）。</summary>
    public void FlashRanges(IEnumerable<(int Start, int Length)> ranges)
    {
        var brush = new SolidColorBrush(Color.FromArgb(160, 255, 160, 60));
        brush.Freeze();
        foreach (var (start, length) in ranges)
        {
            var s = PointerAt(start);
            var e = PointerAt(start + length);
            if (s != null && e != null) new TextRange(s, e).ApplyPropertyValue(TextElement.BackgroundProperty, brush);
        }
    }

    private TextPointer? PointerAt(int charOffset)
    {
        var p = Rtb.Document.ContentStart;
        int count = 0;
        while (p != null)
        {
            var ctx = p.GetPointerContext(LogicalDirection.Forward);
            switch (ctx)
            {
                case TextPointerContext.Text:
                {
                    var runText = p.GetTextInRun(LogicalDirection.Forward);
                    if (count + runText.Length >= charOffset)
                        return p.GetPositionAtOffset(charOffset - count);
                    count += runText.Length;
                    p = p.GetPositionAtOffset(runText.Length);
                    break;
                }
                case TextPointerContext.ElementStart:
                {
                    if (p.GetAdjacentElement(LogicalDirection.Forward) is LineBreak)
                    {
                        if (count >= charOffset) return p;
                        count++;
                    }
                    p = p.GetNextContextPosition(LogicalDirection.Forward);
                    break;
                }
                case TextPointerContext.ElementEnd:
                {
                    if (p.Parent is Paragraph para && para.NextBlock != null)
                    {
                        if (count >= charOffset) return p;
                        count++;
                    }
                    p = p.GetNextContextPosition(LogicalDirection.Forward);
                    break;
                }
                case TextPointerContext.None:
                    return Rtb.Document.ContentEnd;
                default:
                    p = p.GetNextContextPosition(LogicalDirection.Forward);
                    break;
            }
        }
        return Rtb.Document.ContentEnd;
    }

    public void FocusEditor() => Rtb.Focus();

    // ------------------------------------------------------------------ 事件

    private void Rtb_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => RaiseEvent(new RoutedEventArgs(ActivatedEvent, this));

    private void Rtb_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => RaiseEvent(new RoutedEventArgs(ActivatedEvent, this));

    private void Rtb_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        RaiseEdited();
    }

    private void RaiseEdited() => RaiseEvent(new RoutedEventArgs(ContentEditedEvent, this));

    private void Request(string action)
    {
        _lastAction = action;
        RaiseEvent(new RoutedEventArgs(ActionRequestedEvent, this));
    }

    private void HighlightMenu_Click(object sender, RoutedEventArgs e) => Request("highlight");
    private void UnhighlightMenu_Click(object sender, RoutedEventArgs e) => UnhighlightSelection();
    private void DictionaryMenu_Click(object sender, RoutedEventArgs e) => Request("dictionary");
    private void SpeakMenu_Click(object sender, RoutedEventArgs e) => Request("speak");
    private void CopyCitationMenu_Click(object sender, RoutedEventArgs e) => Request("copy-citation");
    private void AiMenu_Click(object sender, RoutedEventArgs e) => Request("ai");
    private void MenuButton_Click(object sender, RoutedEventArgs e) => Request("menu");
}
