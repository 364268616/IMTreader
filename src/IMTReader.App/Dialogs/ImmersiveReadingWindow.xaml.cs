using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using IMTReader.App.ViewModels;
using IMTReader.Core.Text;

namespace IMTReader.App.Dialogs;

public partial class ImmersiveReadingWindow : Window
{
    private readonly DocumentViewModel _doc;
    private int _page;
    private double _fontSize = 22;
    private bool _ready;
    private string _text = string.Empty;

    public ImmersiveReadingWindow(DocumentViewModel doc)
    {
        InitializeComponent();
        _doc = doc;
        _page = doc.CurrentPage;
        Title = $"沉浸阅读 — {doc.Title}";
        var s = AppServices.Settings.Current;
        _fontSize = Math.Max(16, s.FontSize + 5);
        FontBox.SelectedIndex = Math.Max(0, new[] { "楷体", "宋体", "仿宋", "华文楷体", "华文宋体", "微软雅黑" }.ToList().IndexOf(s.FontFamily));
        ThemeBox.SelectedIndex = (int)s.Theme;
        SimplifiedToggle.IsChecked = doc.ShowSimplified;
        _ready = true;
        ApplyTheme();
        ShowPage(_page);
    }

    public void ShowPage(int page)
    {
        _page = Math.Clamp(page, 0, Math.Max(0, _doc.PageCount - 1));
        var blocks = AppServices.Db.GetPageBlocks(_doc.Document.Id, _page);
        _text = blocks.Count == 0
            ? "（本页尚未识别，请先在阅读界面执行“单页识别”）"
            : string.Join("\n\n", blocks.Select(b => b.Text));
        PageText.Text = $"第 {_page + 1} / {_doc.PageCount} 页";
        Render();
    }

    private void Render()
    {
        if (!_ready) return;
        var text = SimplifiedToggle.IsChecked == true ? ChineseConverter.ToSimplified(_text) : _text;
        var font = new FontFamily(((ComboBoxItem)FontBox.SelectedItem)?.Content?.ToString() ?? "楷体");
        bool vertical = VerticalToggle.IsChecked == true;
        VerticalPanel.Visibility = vertical ? Visibility.Visible : Visibility.Collapsed;
        HorizontalText.Visibility = vertical ? Visibility.Collapsed : Visibility.Visible;
        if (vertical)
        {
            VerticalPanel.Text = text;
            VerticalPanel.FontSize = _fontSize;
            VerticalPanel.FontFamily = font;
            VerticalPanel.ShowColumnLines = LinesToggle.IsChecked == true;
            Scroll.ScrollToRightEnd();
        }
        else
        {
            HorizontalText.Text = text;
            HorizontalText.FontSize = _fontSize;
            HorizontalText.LineHeight = _fontSize * 1.8;
            HorizontalText.FontFamily = font;
            Scroll.ScrollToTop();
        }
    }

    private void ApplyTheme()
    {
        (Color bg, Color fg) = ThemeBox.SelectedIndex switch
        {
            2 => (Color.FromRgb(0x1F, 0x24, 0x21), Color.FromRgb(0xE6, 0xE1, 0xD6)),
            1 => (Colors.White, Color.FromRgb(0x22, 0x22, 0x22)),
            _ => (Color.FromRgb(0xF7, 0xF3, 0xE8), Color.FromRgb(0x2B, 0x26, 0x20))
        };
        var bgBrush = new SolidColorBrush(bg);
        var fgBrush = new SolidColorBrush(fg);
        Background = bgBrush;
        Scroll.Background = bgBrush;
        VerticalPanel.Foreground = fgBrush;
        HorizontalText.Foreground = fgBrush;
    }

    private void Prev_Click(object sender, RoutedEventArgs e) => ShowPage(_page - 1);
    private void Next_Click(object sender, RoutedEventArgs e) => ShowPage(_page + 1);

    private void Smaller_Click(object sender, RoutedEventArgs e)
    {
        _fontSize = Math.Max(12, _fontSize - 2);
        Render();
    }

    private void Larger_Click(object sender, RoutedEventArgs e)
    {
        _fontSize = Math.Min(60, _fontSize + 2);
        Render();
    }

    private void FontBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => Render();

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        ApplyTheme();
    }

    private void Option_Changed(object sender, RoutedEventArgs e) => Render();

    private void Scroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // 竖排模式下滚轮横向滚动（从右向左阅读）
        if (VerticalToggle.IsChecked == true && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            Scroll.ScrollToHorizontalOffset(Scroll.HorizontalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(SimplifiedToggle.IsChecked == true ? ChineseConverter.ToSimplified(_text) : _text); } catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key is Key.Left or Key.PageDown) { ShowPage(_page + 1); e.Handled = true; }
        else if (e.Key is Key.Right or Key.PageUp) { ShowPage(_page - 1); e.Handled = true; }
        else if (e.Key == Key.Escape) Close();
    }
}
