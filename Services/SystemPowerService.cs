using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PowerMonitor.Services;

/// <summary>
/// 系统电源操作服务：封装 Win32 关机/重启/睡眠/休眠 API。
/// <para>
/// 关机、重启使用 user32!ExitWindowsEx，调用前先为本进程令牌启用
/// SeShutdownPrivilege 特权；睡眠/休眠使用 powrprof!SetSuspendState。
/// 所有失败都抛出带 Win32 错误码的 <see cref="Win32Exception"/>，由 UI 层提示。
/// </para>
/// </summary>
public sealed class SystemPowerService
{
    // ExitWindowsEx 标志
    private const uint EwxShutdown = 0x00000001;
    private const uint EwxReboot = 0x00000002;
    private const uint EwxForceIfHung = 0x00000010;

    // 关机原因码：应用程序发起 | 其他原因 | 计划性
    private const uint ShutdownReason =
        0x00040000 | // SHTDN_REASON_MAJOR_APPLICATION
        0x00000000 | // SHTDN_REASON_MINOR_OTHER
        0x80000000;  // SHTDN_REASON_FLAG_PLANNED

    // 令牌访问掩码与特权属性
    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const int SePrivilegeEnabled = 0x00000002;
    private const string SeShutdownName = "SeShutdownPrivilege";

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.U1)] byte hibernate,
        [MarshalAs(UnmanagedType.U1)] byte forceCritical,
        [MarshalAs(UnmanagedType.U1)] byte disableWakeEvent);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        nint processHandle, uint desiredAccess, out nint tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(
        string? systemName, string privilegeName, ref Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        nint tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        ref TokenPrivileges newState,
        uint zero,
        nint null1,
        nint null2);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    /// <summary>立即关机（仅强制结束已挂起的程序，正常程序会收到退出通知）。</summary>
    public void Shutdown() => ExitWindows(EwxShutdown, "关机");

    /// <summary>立即重启。</summary>
    public void Restart() => ExitWindows(EwxReboot, "重启");

    /// <summary>使计算机进入睡眠（S3/现代待机，保持内存供电，可快速恢复）。</summary>
    public void Sleep() => Suspend(hibernate: false, "睡眠");

    /// <summary>使计算机进入休眠（将内存内容写入磁盘后完全断电）。休眠未启用时会抛出异常。</summary>
    public void Hibernate() => Suspend(hibernate: true, "休眠");

    private static void ExitWindows(uint flags, string action)
    {
        EnableShutdownPrivilege();

        if (!ExitWindowsEx(flags | EwxForceIfHung, ShutdownReason))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"{action}失败：ExitWindowsEx 调用失败");
        }
    }

    private static void Suspend(bool hibernate, string action)
    {
        if (!SetSuspendState(hibernate ? (byte)1 : (byte)0, 0, 0))
        {
            var error = Marshal.GetLastWin32Error();
            var hint = hibernate
                ? "（若系统未启用休眠，可以管理员身份执行 powercfg /hibernate on 后重试）"
                : string.Empty;
            throw new Win32Exception(error, $"{action}失败：SetSuspendState 调用失败{hint}");
        }
    }

    /// <summary>
    /// 为当前进程令牌启用 SeShutdownPrivilege。
    /// 交互式用户默认持有该特权但处于禁用状态，必须先启用才能调用 ExitWindowsEx。
    /// </summary>
    private static void EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(
                GetCurrentProcess(),
                TokenAdjustPrivileges | TokenQuery,
                out var token))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "关机失败：无法打开当前进程的访问令牌");
        }

        try
        {
            var tp = new TokenPrivileges
            {
                PrivilegeCount = 1,
                Luid = default,
                Attributes = SePrivilegeEnabled
            };

            if (!LookupPrivilegeValue(null, SeShutdownName, ref tp.Luid))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "关机失败：系统中找不到 SeShutdownPrivilege 特权");
            }

            if (!AdjustTokenPrivileges(token, false, ref tp, 0, nint.Zero, nint.Zero))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "关机失败：AdjustTokenPrivileges 调用失败");
            }

            // 该 API 即使特权未被授予也可能返回 true，需要通过 GetLastError 区分。
            var lastError = Marshal.GetLastWin32Error();
            if (lastError != 0)
            {
                throw new Win32Exception(
                    lastError,
                    "关机失败：当前账户没有关机特权（ERROR_NOT_ALL_ASSIGNED）");
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public int PrivilegeCount;
        public Luid Luid;
        public int Attributes;
    }
}
