using System.Windows;
using System.IO;
namespace IndustrialVision.Wpf;
public partial class App : Application
{
    private static readonly object LogLock = new();
    public static string LogPath { get; } = FindLogPath();

    private static string FindLogPath()
    {
        var folder = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(folder, "IndustrialVision.sln")) && Directory.GetParent(folder) is { } parent)
            folder = parent.FullName;
        if (!File.Exists(Path.Combine(folder, "IndustrialVision.sln"))) folder = Path.GetTempPath();
        return Path.Combine(folder, "artifacts", "logs", $"vision-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
    }

    public static void WriteLog(string message)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"{DateTime.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, error) =>
        {
            WriteLog("UI 未处理异常：" + error.Exception);
            error.Handled = true;
            MessageBox.Show($"程序发生异常：{error.Exception.Message}\n\n详细日志：{LogPath}", "程序异常");
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, error) => WriteLog("进程未处理异常：" + error.ExceptionObject);
        WriteLog($"启动：{Environment.ProcessPath}；程序集：{typeof(App).Assembly.Location}");
        base.OnStartup(e);
    }
}
