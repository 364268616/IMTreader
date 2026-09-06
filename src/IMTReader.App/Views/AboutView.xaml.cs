using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using IMTReader.Core.Ocr;
using IMTReader.Core.Services;

namespace IMTReader.App.Views;

public partial class AboutView : UserControl, IActivatableView
{
    public AboutView()
    {
        InitializeComponent();
        var asm = Assembly.GetExecutingAssembly();
        ProductText.Text = App.ProductName;
        VersionText.Text = asm.GetName().Version?.ToString(3) ?? "1.0.0";
        DeveloperText.Text = Get<AssemblyCompanyAttribute>(asm, a => a.Company) ?? "广哥";
        CopyrightText.Text = Get<AssemblyCopyrightAttribute>(asm, a => a.Copyright) ?? string.Empty;
        RuntimeText.Text = $".NET {Environment.Version.ToString(3)} · WPF · Windows x64";
    }

    public void OnActivated()
    {
        EngineText.Text = PaddleOcrEngine.IsRuntimeAvailable()
            ? "PaddleOCR PP-OCRv5（本机离线，模型随程序分发）"
            : "PaddleOCR 模型缺失，请检查程序目录下的 OcrModels";
    }

    private static string? Get<T>(Assembly asm, Func<T, string?> select) where T : Attribute
    {
        var attr = asm.GetCustomAttribute<T>();
        var value = attr == null ? null : select(attr);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private void Notices_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
        if (!File.Exists(path))
        {
            MessageBox.Show(Window.GetWindow(this), "未找到第三方组件声明文件：" + path, "关于", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Open(path);
    }

    private void DataDir_Click(object sender, RoutedEventArgs e) => Open(AppPaths.DataDir);

    private void LogDir_Click(object sender, RoutedEventArgs e) => Open(AppPaths.LogDir);

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Open(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppServices.Log.Warn("打开失败：" + ex.Message);
        }
    }
}
