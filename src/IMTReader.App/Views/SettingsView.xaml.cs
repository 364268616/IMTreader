using System.Windows;
using System.Windows.Controls;
using IMTReader.App.ViewModels;
using Microsoft.Win32;

namespace IMTReader.App.Views;

public partial class SettingsView : UserControl, IActivatableView
{
    private readonly SettingsViewModel _vm = new();

    public SettingsView()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    public void OnActivated() => _vm.Reload();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _vm.Save();
        _vm.TestResult = "已保存";
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => _vm.Reload();

    private void PickModelDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "选择 PP-OCR 模型目录（其中应含 *_det_infer、*_rec_infer 子目录）" };
        if (dlg.ShowDialog(Window.GetWindow(this)) == true) _vm.OcrModelDirectory = dlg.FolderName;
    }

    private void ResetModelDir_Click(object sender, RoutedEventArgs e) => _vm.OcrModelDirectory = string.Empty;

    private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetBox.SelectedItem is string preset) _vm.ApplyPreset(preset);
    }

    private async void TestAi_Click(object sender, RoutedEventArgs e)
    {
        _vm.Save();
        _vm.TestResult = "正在测试…";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var reply = await AppServices.Ai.TestAsync(cts.Token);
            _vm.TestResult = "服务回复：" + (reply.Length > 60 ? reply[..60] + "…" : reply);
        }
        catch (Exception ex)
        {
            _vm.TestResult = "失败：" + ex.Message;
        }
    }
}
