using Microsoft.Win32;

namespace PowerMonitor.Services;

/// <summary>
/// 管理开机自启动：通过 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 注册表项
/// 添加或移除自启项。无需管理员权限，仅影响当前用户。
/// </summary>
public sealed class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "PowerMonitor";

    /// <summary>当前 exe 的完整路径。</summary>
    private static string ExecutablePath =>
        Environment.ProcessPath ?? Application.ExecutablePath;

    /// <summary>自启是否已启用。</summary>
    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(AppName) is string v
            && string.Equals(v, ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>启用开机自启。失败时抛出异常由 UI 提示。</summary>
    public void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue(AppName, ExecutablePath, RegistryValueKind.String);
        AppLog.Log($"已启用开机自启：{ExecutablePath}");
    }

    /// <summary>禁用开机自启。</summary>
    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key?.GetValue(AppName) is not null)
        {
            key.DeleteValue(AppName, false);
        }
        AppLog.Log("已禁用开机自启");
    }
}
