using CommunityToolkit.Mvvm.ComponentModel;
using IMTReader.App.Controls;
using IMTReader.Core.Models;
using IMTReader.Core.Text;

namespace IMTReader.App.ViewModels;

/// <summary>一个文本块在右侧文本面板中的表示。</summary>
public sealed class BlockViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _showSimplified;
    private List<Highlight> _highlights;

    public OcrBlock Block { get; private set; }

    /// <summary>由 BlockEditor 在加载时回填，供保存/高亮等操作访问编辑器。</summary>
    public BlockEditor? Editor { get; set; }

    public BlockViewModel(OcrBlock block, IEnumerable<Highlight> highlights)
    {
        Block = block;
        _highlights = highlights.ToList();
    }

    public long Id => Block.Id;
    public int Order => Block.Order;
    public string Label => $"[{Block.Order + 1}]";
    public string OrientationText => Block.IsVertical ? "竖排" : "横排";
    public bool IsVertical => Block.IsVertical;
    public string ScoreText => Block.Score > 0 ? $"{Block.Score:P0}" : string.Empty;

    /// <summary>原文（编辑后由编辑器写回）。</summary>
    public string Text => Block.Text;

    /// <summary>显示文本：繁→简开启时显示简体。</summary>
    public string DisplayText => _showSimplified ? ChineseConverter.ToSimplified(Block.Text) : Block.Text;

    public IReadOnlyList<Highlight> Highlights => _highlights;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool ShowSimplified
    {
        get => _showSimplified;
        set
        {
            if (SetProperty(ref _showSimplified, value))
                OnPropertyChanged(nameof(DisplayText));
        }
    }

    public void UpdateFromModel(OcrBlock block, IEnumerable<Highlight> highlights)
    {
        Block = block;
        _highlights = highlights.ToList();
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Highlights));
        Editor?.Reload();
    }

    public void SetText(string text)
    {
        Block.Text = text;
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(DisplayText));
    }

    public void SetHighlights(IEnumerable<Highlight> highlights)
    {
        _highlights = highlights.ToList();
        OnPropertyChanged(nameof(Highlights));
    }
}
