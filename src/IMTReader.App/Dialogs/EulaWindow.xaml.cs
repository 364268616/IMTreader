using System.IO;
using System.Windows;

namespace IMTReader.App.Dialogs;

public partial class EulaWindow : Window
{
    public const string CurrentVersion = "1.0";

    public EulaWindow()
    {
        InitializeComponent();
        Title = "用户协议与免责声明 — " + App.ProductName;
        BodyBox.Text = LoadText();
    }

    public static bool IsAccepted() =>
        AppServices.Settings.Current.EulaAcceptedVersion == CurrentVersion;

    /// <summary>若尚未同意当前版本协议，弹出窗口；同意则写入设置。返回是否允许继续运行。</summary>
    public static bool EnsureAccepted()
    {
        if (IsAccepted()) return true;
        var dlg = new EulaWindow();
        return dlg.ShowDialog() == true;
    }

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "用户协议.txt");

    public static string LoadText()
    {
        try
        {
            if (File.Exists(FilePath))
                return File.ReadAllText(FilePath);
        }
        catch
        {
            // 下面给出短文，避免缺文件时无法展示任何条款
        }
        return "未找到用户协议文件（用户协议.txt）。请重新安装本软件。在您同意协议之前，程序不会启动。";
    }

    private void AgreeBox_Changed(object sender, RoutedEventArgs e) =>
        AcceptButton.IsEnabled = AgreeBox.IsChecked == true && File.Exists(FilePath);

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (AgreeBox.IsChecked != true || !File.Exists(FilePath)) return;
        AppServices.Settings.Update(s => s.EulaAcceptedVersion = CurrentVersion);
        DialogResult = true;
        Close();
    }

    private void Decline_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
