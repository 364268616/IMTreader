using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using IMTReader.Core.Models;

namespace IMTReader.App.Controls;

/// <summary>
/// 原图查看器：缩放、旋转、拖拽平移、文本块框叠加、点击选块、框选区域。
/// 叠加层使用图像像素坐标系，随图像一起缩放旋转。
/// </summary>
public sealed class PageViewer : ContentControl
{
    private readonly ScrollViewer _scroll;
    private readonly Grid _content;
    private readonly Image _image;
    private readonly Canvas _overlay;
    private readonly RotateTransform _rotate = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Rectangle _selection;
    private readonly TextBlock _emptyHint;
    private readonly Dictionary<long, Rectangle> _boxes = new();

    private Point _dragStart;
    private Point _dragScrollStart;
    private bool _dragging;
    private bool _selecting;
    private Point _selectStart;
    private bool _moved;

    public event Action<OcrBlock>? BlockClicked;
    public event Action<Rect>? RegionSelected;
    public event Action<double>? ZoomChanged;

    public PageViewer()
    {
        _image = new Image { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _overlay = new Canvas { Background = Brushes.Transparent };
        _selection = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(230, 120, 20)),
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(40, 230, 120, 20)),
            Visibility = Visibility.Collapsed
        };
        _overlay.Children.Add(_selection);

        var group = new TransformGroup();
        group.Children.Add(_rotate);
        group.Children.Add(_scale);
        _content = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24),
            LayoutTransform = group
        };
        _content.Children.Add(_image);
        _content.Children.Add(_overlay);

        _emptyHint = new TextBlock
        {
            Text = "在左侧文献库导入扫描件后打开阅读",
            Foreground = Brushes.WhiteSmoke,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = (Brush)Application.Current.Resources["ViewerBackgroundBrush"],
            Focusable = true,
            Content = new Grid { Children = { _content, _emptyHint } }
        };
        Content = _scroll;
        Focusable = true;

        _scroll.PreviewMouseWheel += OnMouseWheel;
        _overlay.MouseLeftButtonDown += OnMouseDown;
        _overlay.MouseMove += OnMouseMove;
        _overlay.MouseLeftButtonUp += OnMouseUp;
        _overlay.Cursor = Cursors.Hand;
    }

    // ------------------------------------------------------------------ 依赖属性

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(BitmapSource), typeof(PageViewer), new PropertyMetadata(null, (d, _) => ((PageViewer)d).OnSourceChanged()));

    public static readonly DependencyProperty BlocksProperty = DependencyProperty.Register(
        nameof(Blocks), typeof(IReadOnlyList<OcrBlock>), typeof(PageViewer), new PropertyMetadata(null, (d, _) => ((PageViewer)d).RebuildBoxes()));

    public static readonly DependencyProperty SelectedBlockProperty = DependencyProperty.Register(
        nameof(SelectedBlock), typeof(OcrBlock), typeof(PageViewer), new PropertyMetadata(null, (d, _) => ((PageViewer)d).UpdateBoxStyles()));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(PageViewer), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((PageViewer)d).OnZoomChanged()));

    public static readonly DependencyProperty RotationProperty = DependencyProperty.Register(
        nameof(Rotation), typeof(int), typeof(PageViewer), new PropertyMetadata(0, (d, _) => ((PageViewer)d).OnRotationChanged()));

    public static readonly DependencyProperty ShowBoxesProperty = DependencyProperty.Register(
        nameof(ShowBoxes), typeof(bool), typeof(PageViewer), new PropertyMetadata(true, (d, _) => ((PageViewer)d).UpdateBoxStyles()));

    public static readonly DependencyProperty IsSelectModeProperty = DependencyProperty.Register(
        nameof(IsSelectMode), typeof(bool), typeof(PageViewer), new PropertyMetadata(false, (d, _) => ((PageViewer)d).OnSelectModeChanged()));

    public BitmapSource? Source
    {
        get => (BitmapSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public IReadOnlyList<OcrBlock>? Blocks
    {
        get => (IReadOnlyList<OcrBlock>?)GetValue(BlocksProperty);
        set => SetValue(BlocksProperty, value);
    }

    public OcrBlock? SelectedBlock
    {
        get => (OcrBlock?)GetValue(SelectedBlockProperty);
        set => SetValue(SelectedBlockProperty, value);
    }

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public int Rotation
    {
        get => (int)GetValue(RotationProperty);
        set => SetValue(RotationProperty, value);
    }

    public bool ShowBoxes
    {
        get => (bool)GetValue(ShowBoxesProperty);
        set => SetValue(ShowBoxesProperty, value);
    }

    public bool IsSelectMode
    {
        get => (bool)GetValue(IsSelectModeProperty);
        set => SetValue(IsSelectModeProperty, value);
    }

    // ------------------------------------------------------------------ 更新

    private void OnSourceChanged()
    {
        var src = Source;
        _image.Source = src;
        _emptyHint.Visibility = src == null ? Visibility.Visible : Visibility.Collapsed;
        if (src == null)
        {
            _image.Width = _image.Height = 0;
            _overlay.Width = _overlay.Height = 0;
            return;
        }
        _image.Width = src.PixelWidth;
        _image.Height = src.PixelHeight;
        _overlay.Width = src.PixelWidth;
        _overlay.Height = src.PixelHeight;
        RebuildBoxes();
    }

    private void OnZoomChanged()
    {
        double z = Math.Clamp(Zoom, 0.05, 8);
        _scale.ScaleX = _scale.ScaleY = z;
        UpdateBoxStyles();
        ZoomChanged?.Invoke(z);
    }

    private void OnRotationChanged() => _rotate.Angle = Rotation;

    private void OnSelectModeChanged() => _overlay.Cursor = IsSelectMode ? Cursors.Cross : Cursors.Hand;

    private void RebuildBoxes()
    {
        foreach (var r in _boxes.Values) _overlay.Children.Remove(r);
        _boxes.Clear();
        var blocks = Blocks;
        if (blocks == null || Source == null) return;
        double w = _overlay.Width, h = _overlay.Height;
        foreach (var b in blocks)
        {
            var rect = new Rectangle
            {
                Width = Math.Max(1, b.W * w),
                Height = Math.Max(1, b.H * h),
                Tag = b,
                ToolTip = $"[{b.Order + 1}] {(b.IsVertical ? "竖排" : "横排")}",
                RadiusX = 2,
                RadiusY = 2
            };
            Canvas.SetLeft(rect, b.X * w);
            Canvas.SetTop(rect, b.Y * h);
            rect.MouseEnter += (s, _) => { if (s is Rectangle r && r.Tag != SelectedBlock) r.Fill = (Brush)Application.Current.Resources["BoxHoverFillBrush"]; };
            rect.MouseLeave += (s, _) => { if (s is Rectangle r) StyleBox(r); };
            _overlay.Children.Insert(0, rect);
            _boxes[b.Id] = rect;
        }
        UpdateBoxStyles();
    }

    private void UpdateBoxStyles()
    {
        foreach (var r in _boxes.Values) StyleBox(r);
    }

    private void StyleBox(Rectangle r)
    {
        bool selected = r.Tag == SelectedBlock;
        double thickness = Math.Max(0.6, 1.8 / Math.Max(0.1, _scale.ScaleX));
        r.Visibility = ShowBoxes || selected ? Visibility.Visible : Visibility.Collapsed;
        r.StrokeThickness = selected ? thickness * 1.6 : thickness;
        r.Stroke = (Brush)Application.Current.Resources[selected ? "BoxSelectedStrokeBrush" : "BoxStrokeBrush"];
        r.Fill = selected ? (Brush)Application.Current.Resources["BoxSelectedFillBrush"] : Brushes.Transparent;
    }

    /// <summary>把某块滚动到可视区域中央。</summary>
    public void ScrollToBlock(OcrBlock block)
    {
        if (Source == null) return;
        _scroll.UpdateLayout();
        double w = _overlay.Width, h = _overlay.Height;
        var center = new Point((block.X + block.W / 2) * w, (block.Y + block.H / 2) * h);
        var transformed = _content.LayoutTransform.Transform(center);
        // 变换后的坐标相对于 content 的布局边界左上角（旋转会产生负值，按边界修正）
        var bounds = _content.LayoutTransform.TransformBounds(new Rect(0, 0, w, h));
        double x = transformed.X - bounds.X + _content.Margin.Left;
        double y = transformed.Y - bounds.Y + _content.Margin.Top;
        double extraX = Math.Max(0, (_scroll.ViewportWidth - (bounds.Width + _content.Margin.Left + _content.Margin.Right)) / 2);
        double extraY = Math.Max(0, (_scroll.ViewportHeight - (bounds.Height + _content.Margin.Top + _content.Margin.Bottom)) / 2);
        _scroll.ScrollToHorizontalOffset(x + extraX - _scroll.ViewportWidth / 2);
        _scroll.ScrollToVerticalOffset(y + extraY - _scroll.ViewportHeight / 2);
    }

    public void FitWidth()
    {
        if (Source == null) return;
        var bounds = _rotate.TransformBounds(new Rect(0, 0, _overlay.Width, _overlay.Height));
        double avail = Math.Max(50, _scroll.ViewportWidth - _content.Margin.Left - _content.Margin.Right - 4);
        Zoom = avail / Math.Max(1, bounds.Width);
    }

    public void FitPage()
    {
        if (Source == null) return;
        var bounds = _rotate.TransformBounds(new Rect(0, 0, _overlay.Width, _overlay.Height));
        double availW = Math.Max(50, _scroll.ViewportWidth - _content.Margin.Left - _content.Margin.Right - 4);
        double availH = Math.Max(50, _scroll.ViewportHeight - _content.Margin.Top - _content.Margin.Bottom - 4);
        Zoom = Math.Min(availW / Math.Max(1, bounds.Width), availH / Math.Max(1, bounds.Height));
    }

    // ------------------------------------------------------------------ 鼠标

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        e.Handled = true;
        double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        var mouse = e.GetPosition(_scroll);
        double oldX = _scroll.HorizontalOffset + mouse.X, oldY = _scroll.VerticalOffset + mouse.Y;
        Zoom = Math.Clamp(Zoom * factor, 0.05, 8);
        _scroll.UpdateLayout();
        _scroll.ScrollToHorizontalOffset(oldX * factor - mouse.X);
        _scroll.ScrollToVerticalOffset(oldY * factor - mouse.Y);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        _moved = false;
        if (IsSelectMode)
        {
            _selecting = true;
            _selectStart = e.GetPosition(_overlay);
            _selection.Visibility = Visibility.Visible;
            Canvas.SetLeft(_selection, _selectStart.X);
            Canvas.SetTop(_selection, _selectStart.Y);
            _selection.Width = _selection.Height = 0;
        }
        else
        {
            _dragging = true;
            _dragStart = e.GetPosition(_scroll);
            _dragScrollStart = new Point(_scroll.HorizontalOffset, _scroll.VerticalOffset);
        }
        _overlay.CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_selecting)
        {
            var p = e.GetPosition(_overlay);
            double x = Math.Min(p.X, _selectStart.X), y = Math.Min(p.Y, _selectStart.Y);
            Canvas.SetLeft(_selection, x);
            Canvas.SetTop(_selection, y);
            _selection.Width = Math.Abs(p.X - _selectStart.X);
            _selection.Height = Math.Abs(p.Y - _selectStart.Y);
            _moved = true;
        }
        else if (_dragging)
        {
            var p = e.GetPosition(_scroll);
            var delta = p - _dragStart;
            if (delta.Length > 3) _moved = true;
            _scroll.ScrollToHorizontalOffset(_dragScrollStart.X - delta.X);
            _scroll.ScrollToVerticalOffset(_dragScrollStart.Y - delta.Y);
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _overlay.ReleaseMouseCapture();
        if (_selecting)
        {
            _selecting = false;
            _selection.Visibility = Visibility.Collapsed;
            if (_moved && _selection.Width > 4 && _selection.Height > 4 && _overlay.Width > 0)
            {
                var rect = new Rect(
                    Canvas.GetLeft(_selection) / _overlay.Width,
                    Canvas.GetTop(_selection) / _overlay.Height,
                    _selection.Width / _overlay.Width,
                    _selection.Height / _overlay.Height);
                RegionSelected?.Invoke(rect);
            }
            return;
        }
        if (_dragging)
        {
            _dragging = false;
            if (!_moved)
            {
                var p = e.GetPosition(_overlay);
                var hit = HitBlock(p);
                if (hit != null) BlockClicked?.Invoke(hit);
            }
        }
    }

    private OcrBlock? HitBlock(Point p)
    {
        var blocks = Blocks;
        if (blocks == null || _overlay.Width <= 0) return null;
        double nx = p.X / _overlay.Width, ny = p.Y / _overlay.Height;
        OcrBlock? best = null;
        double bestArea = double.MaxValue;
        foreach (var b in blocks)
        {
            if (nx >= b.X && nx <= b.X + b.W && ny >= b.Y && ny <= b.Y + b.H)
            {
                double area = b.W * b.H;
                if (area < bestArea)
                {
                    bestArea = area;
                    best = b;
                }
            }
        }
        return best;
    }
}
