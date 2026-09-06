using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using IMTReader.Core.Ocr;

namespace IMTReader.App.Views;

public sealed class TaskRowViewModel : ObservableObject
{
    public OcrJob Job { get; }

    public TaskRowViewModel(OcrJob job)
    {
        Job = job;
    }

    public string DocumentTitle => Job.Document.Title;
    public string Title => Job.Title;
    public string Mode => Job.Options.Describe();
    public string Status => Job.StatusText;
    public double Progress => Job.Progress;
    public string CreatedAt => Job.CreatedAt.ToString("MM-dd HH:mm:ss");
    public string Error => Job.Error ?? string.Empty;

    public string Elapsed
    {
        get
        {
            if (Job.StartedAt == null) return "—";
            var end = Job.FinishedAt ?? DateTime.Now;
            var t = end - Job.StartedAt.Value;
            return t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} 分 {t.Seconds} 秒" : $"{t.Seconds} 秒";
        }
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}

public partial class TaskCenterView : UserControl, IActivatableView, IDisposable
{
    private readonly ObservableCollection<TaskRowViewModel> _rows = new();

    public TaskCenterView()
    {
        InitializeComponent();
        Grid.DataContext = _rows;
        Grid.ItemsSource = _rows;
        AppServices.Jobs.JobChanged += OnJobChanged;
        Reload();
    }

    public void OnActivated()
    {
        Reload();
        var s = AppServices.Settings.Current;
        EngineText.Text = "当前引擎：" + s.OcrEngine switch
        {
            Core.Models.OcrEngineKind.WindowsBuiltIn => "Windows OCR",
            Core.Models.OcrEngineKind.VisionLlm => "AI 视觉模型",
            _ => "PaddleOCR（本地）"
        };
    }

    private void Reload()
    {
        _rows.Clear();
        foreach (var job in AppServices.Jobs.Jobs.OrderByDescending(j => j.CreatedAt)) _rows.Add(new TaskRowViewModel(job));
    }

    private void OnJobChanged(OcrJob job)
    {
        var row = _rows.FirstOrDefault(r => r.Job.Id == job.Id);
        if (row == null) _rows.Insert(0, new TaskRowViewModel(job));
        else row.Refresh();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is TaskRowViewModel row) AppServices.Jobs.Cancel(row.Job.Id);
    }

    private void ClearFinished_Click(object sender, RoutedEventArgs e)
    {
        AppServices.Jobs.ClearFinished();
        Reload();
    }

    public void Dispose() => AppServices.Jobs.JobChanged -= OnJobChanged;
}
