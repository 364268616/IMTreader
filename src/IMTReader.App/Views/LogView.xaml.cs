using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using IMTReader.Core.Services;

namespace IMTReader.App.Views;

public partial class LogView : UserControl, IActivatableView, IDisposable
{
    private readonly ObservableCollection<LogEntry> _entries = new();

    public LogView()
    {
        InitializeComponent();
        foreach (var e in AppServices.Log.Snapshot().Reverse()) _entries.Add(e);
        Grid.ItemsSource = _entries;
        AppServices.Log.EntryAdded += OnEntry;
    }

    private void OnEntry(LogEntry entry)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _entries.Insert(0, entry);
            while (_entries.Count > 1000) _entries.RemoveAt(_entries.Count - 1);
        });
    }

    public void OnActivated()
    {
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => _entries.Clear();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(string.Join(Environment.NewLine, _entries.Reverse().Select(x => x.ToString()))); }
        catch { }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start("explorer.exe", $"\"{AppPaths.LogDir}\""); }
        catch (Exception ex) { AppServices.Log.Warn("打开日志文件夹失败：" + ex.Message); }
    }

    public void Dispose() => AppServices.Log.EntryAdded -= OnEntry;
}
