using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using IMTReader.Core.Models;
using IMTReader.Core.Ocr;

namespace IMTReader.App.ViewModels;

public enum SectionKind
{
    Library,
    Tasks,
    History,
    Settings,
    Log,
    About
}

public abstract class TabViewModel : ObservableObject
{
    private bool _isSelected;

    public abstract string Title { get; }
    public abstract bool IsClosable { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class SectionTabViewModel : TabViewModel
{
    public SectionKind Kind { get; }

    public SectionTabViewModel(SectionKind kind)
    {
        Kind = kind;
    }

    public override string Title => Kind switch
    {
        SectionKind.Library => "文献库",
        SectionKind.Tasks => "任务中心",
        SectionKind.History => "历史",
        SectionKind.Settings => "设置",
        SectionKind.About => "关于",
        _ => "日志"
    };

    public override bool IsClosable => Kind != SectionKind.Library;
}

public sealed class DocumentTabViewModel : TabViewModel
{
    public DocumentViewModel Document { get; }

    public DocumentTabViewModel(DocumentViewModel document)
    {
        Document = document;
    }

    public override string Title => Document.Title;
    public override bool IsClosable => true;
}

public sealed class MainViewModel : ObservableObject
{
    private TabViewModel? _selectedTab;
    private bool _hasActiveJob;
    private string _activeJobTitle = string.Empty;
    private string _activeJobStatus = string.Empty;
    private double _activeJobProgress;

    public ObservableCollection<TabViewModel> Tabs { get; } = new();

    public event Action<TabViewModel>? TabOpened;
    public event Action<TabViewModel>? TabClosed;

    public MainViewModel()
    {
        AppServices.Jobs.JobChanged += OnJobChanged;
        ShowSection(SectionKind.Library);
    }

    public TabViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (_selectedTab == value) return;
            if (_selectedTab != null) _selectedTab.IsSelected = false;
            _selectedTab = value;
            if (_selectedTab != null) _selectedTab.IsSelected = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentSection));
        }
    }

    /// <summary>当前选中的功能页；打开文献时为 null，导航栏不高亮任何项。</summary>
    public SectionKind? CurrentSection => (_selectedTab as SectionTabViewModel)?.Kind;

    // ---- 后台识别任务指示 ----

    public bool HasActiveJob
    {
        get => _hasActiveJob;
        private set => SetProperty(ref _hasActiveJob, value);
    }

    public string ActiveJobTitle
    {
        get => _activeJobTitle;
        private set => SetProperty(ref _activeJobTitle, value);
    }

    public string ActiveJobStatus
    {
        get => _activeJobStatus;
        private set => SetProperty(ref _activeJobStatus, value);
    }

    public double ActiveJobProgress
    {
        get => _activeJobProgress;
        private set => SetProperty(ref _activeJobProgress, value);
    }

    public void ShowSection(SectionKind kind)
    {
        var tab = Tabs.OfType<SectionTabViewModel>().FirstOrDefault(t => t.Kind == kind);
        if (tab == null)
        {
            tab = new SectionTabViewModel(kind);
            Tabs.Add(tab);
            TabOpened?.Invoke(tab);
        }
        SelectedTab = tab;
    }

    public DocumentViewModel? FindOpenDocument(long documentId) =>
        Tabs.OfType<DocumentTabViewModel>().FirstOrDefault(t => t.Document.Document.Id == documentId)?.Document;

    public DocumentViewModel OpenDocument(DocumentInfo doc)
    {
        var existing = Tabs.OfType<DocumentTabViewModel>().FirstOrDefault(t => t.Document.Document.Id == doc.Id);
        if (existing != null)
        {
            SelectedTab = existing;
            return existing.Document;
        }
        var vm = new DocumentViewModel(doc);
        var tab = new DocumentTabViewModel(vm);
        Tabs.Add(tab);
        TabOpened?.Invoke(tab);
        SelectedTab = tab;
        AppServices.Log.Info($"打开文献：《{doc.Title}》（{doc.PageCount} 页）");
        return vm;
    }

    public void CloseTab(TabViewModel tab)
    {
        if (!tab.IsClosable) return;
        int idx = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        TabClosed?.Invoke(tab);
        if (tab is DocumentTabViewModel d)
        {
            if (d.Document.IsDirty) d.Document.SaveChanges();
            d.Document.Dispose();
        }
        if (SelectedTab == tab || SelectedTab == null)
            SelectedTab = Tabs.ElementAtOrDefault(Math.Clamp(idx - 1, 0, Math.Max(0, Tabs.Count - 1)));
    }

    public void CloseDocument(long documentId)
    {
        var tab = Tabs.OfType<DocumentTabViewModel>().FirstOrDefault(t => t.Document.Document.Id == documentId);
        if (tab != null) CloseTab(tab);
    }

    private void OnJobChanged(OcrJob job)
    {
        var active = AppServices.Jobs.Jobs.FirstOrDefault(j => j.Status == OcrJobStatus.Running)
                     ?? AppServices.Jobs.Jobs.FirstOrDefault(j => !j.IsFinished);
        HasActiveJob = active != null;
        if (active == null) return;
        ActiveJobTitle = active.Document.Title;
        ActiveJobStatus = active.StatusText;
        ActiveJobProgress = active.Progress;
    }
}
