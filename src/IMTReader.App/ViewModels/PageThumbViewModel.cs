using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace IMTReader.App.ViewModels;

public sealed class PageThumbViewModel : ObservableObject
{
    private BitmapSource? _image;
    private bool _hasOcr;
    private bool _isCurrent;
    private bool _isBookmarked;

    public int PageIndex { get; }
    public int PageNumber => PageIndex + 1;

    public PageThumbViewModel(int pageIndex)
    {
        PageIndex = pageIndex;
    }

    public BitmapSource? Image
    {
        get => _image;
        set => SetProperty(ref _image, value);
    }

    public bool HasOcr
    {
        get => _hasOcr;
        set => SetProperty(ref _hasOcr, value);
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    public bool IsBookmarked
    {
        get => _isBookmarked;
        set => SetProperty(ref _isBookmarked, value);
    }

    public bool IsLoading { get; set; }
}
