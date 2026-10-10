using ImageViewer.App.Services;
using Microsoft.UI.Xaml;

namespace ImageViewer.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var commandLine = Environment.GetCommandLineArgs().Skip(1).ToArray();

        // 纯命令行操作：注册/撤销文件关联后直接退出，不弹主窗口。
        // 右键菜单里也有同样的入口，这里是给愿意用脚本/命令行的场景。
        if (commandLine.Contains("--register-file-associations"))
        {
            FileAssociationService.RegisterAll();
            Environment.Exit(0);
            return;
        }

        if (commandLine.Contains("--unregister-file-associations"))
        {
            FileAssociationService.UnregisterAll();
            Environment.Exit(0);
            return;
        }

        _window = new MainWindow();
        _window.Activate();

        // 「用简壹图片打开」或命令行传入的文件。
        // unpackaged 应用拿不到 AppActivationArguments 里的文件列表，直接读命令行即可。
        if (commandLine.Length > 0)
        {
            _ = _window.OpenFromCommandLineAsync(commandLine);
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ImageViewer");
            Directory.CreateDirectory(logDirectory);
            File.AppendAllText(
                Path.Combine(logDirectory, "crash.log"),
                $"[{DateTimeOffset.Now:O}] Unhandled WinUI exception\r\n{args.Exception}\r\nHandled: {args.Handled}\r\n\r\n");
        }
        catch (Exception)
        {
            // 崩溃记录失败不能覆盖原始 UI 异常。
        }
    }
}
