using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using IMTReader.App.Views;
using IMTReader.Core.Services;

namespace IMTReader.App;

public partial class App : Application
{
    /// <summary>产品名，取自程序集元数据（在 Directory.Build.props 中修改）。</summary>
    public static string ProductName { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "广哥古籍阅读器";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppServices.Log.Error("后台任务异常", args.Exception);
            args.SetObserved();
        };

        try
        {
            AppServices.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show("初始化失败：" + ex.Message, ProductName, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        ThemeManager.Apply(AppServices.Settings.Current);
        AppServices.Settings.Changed += s => ThemeManager.Apply(s);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        if (e.Args.Length > 0)
            window.ImportFromCommandLine(e.Args);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppServices.Log.Error("未处理的异常", e.Exception);
        MessageBox.Show("发生错误：" + e.Exception.Message, ProductName, MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppServices.Shutdown();
        base.OnExit(e);
    }
}
