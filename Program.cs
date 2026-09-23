using PowerMonitor.Services;
using PowerMonitor.Views;

namespace PowerMonitor;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 全局异常与生命周期落盘，便于诊断睡眠/唤醒后进程异常消失等问题。
        AppLog.Log("================ 应用启动 ================");
        Application.ThreadException += (_, e) =>
            AppLog.Log("UI 线程未处理异常 (Application.ThreadException)", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Log("进程未处理异常 (AppDomain.UnhandledException, terminating="
                       + e.IsTerminating + ")", e.ExceptionObject as Exception
                       ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown"));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Log("未观察的任务异常 (TaskScheduler.UnobservedTaskException)", e.Exception);
            e.SetObserved();
        };
        Application.ApplicationExit += (_, _) => AppLog.Log("应用正常退出 (ApplicationExit)");

        ApplicationConfiguration.Initialize();

        // 服务生命周期跟随应用程序，退出时自动解绑系统事件并释放资源。
        using var service = new PowerStateService();
        service.Start();
        AppLog.Log("电源监听服务已启动");

        Application.Run(new MainForm(service));
    }
}
