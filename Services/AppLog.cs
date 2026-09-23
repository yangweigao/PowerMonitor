using System.Text;

namespace PowerMonitor.Services;

/// <summary>
/// 极简文件日志：用于诊断进程级生命周期事件（启动/退出/未处理异常/窗体关闭原因）。
/// 日志位于 %LOCALAPPDATA%\PowerMonitor\app.log，全进程静态可用、线程安全。
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PowerMonitor",
        "app.log");

    static AppLog()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        }
        catch
        {
            // 目录创建失败时静默降级（日志仅用于诊断，不能反向影响主程序）。
        }
    }

    public static void Log(string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.CurrentManagedThreadId,2}] {message}{Environment.NewLine}";
        try
        {
            lock (Gate)
            {
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // 日志失败不应影响监控主流程。
        }
    }

    public static void Log(string context, Exception ex) =>
        Log($"{context} => {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
}
