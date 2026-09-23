using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PowerMonitor.Models;

namespace PowerMonitor.Services;

/// <summary>
/// 电源状态监控服务（纯事件驱动，无轮询）。
/// <para>
/// 启动时创建一个隐藏的原生窗口，由 Windows 在电源状态变化时向其推送
/// <c>WM_POWERBROADCAST</c> 消息（睡眠/恢复、交流电池切换、电量变化、低电量等）；
/// 同时通过 <c>RegisterPowerSettingNotification</c> 订阅显示器开关与合盖动作，
/// 系统以 <c>PBT_POWERSETTINGCHANGE</c> 推送这两类细粒度事件。
/// 收到状态类事件时再调用 kernel32!GetSystemPowerStatus 抓取最新状态快照。
/// </para>
/// </summary>
public sealed class PowerStateService : IDisposable
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(ref SYSTEM_POWER_STATUS lpSystemPowerStatus);

    private readonly object _gate = new();
    private PowerMessageWindow? _window;
    private bool _disposed;

    /// <summary>收到任意系统电源广播消息时触发（回调位于 UI 线程）。</summary>
    public event EventHandler<PowerStateChangedEventArgs>? PowerModeChanged;

    /// <summary>供电/电池状态发生变化（交流切换、电量变化、充电状态变化等）时触发。</summary>
    public event EventHandler<PowerStatusInfo>? PowerStatusChanged;

    /// <summary>
    /// 启动监听。必须在带有消息循环的 UI（STA）线程上调用，窗口过程依赖该消息泵接收广播。
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_window is not null) return;

            var window = new PowerMessageWindow();
            window.PowerBroadcast += OnPowerBroadcast;
            _window = window;
        }
    }

    /// <summary>停止监听并销毁隐藏窗口。</summary>
    public void Stop()
    {
        lock (_gate)
        {
            DestroyWindow();
        }
    }

    /// <summary>主动获取一次当前电源状态快照（供界面初始化/手动刷新使用，非轮询）。</summary>
    public PowerStatusInfo? GetCurrentStatus()
    {
        var native = new SYSTEM_POWER_STATUS();
        if (!GetSystemPowerStatus(ref native))
        {
            var error = Marshal.GetLastWin32Error();
            Debug.WriteLine($"GetSystemPowerStatus 调用失败，Win32Error={error}");
            return null;
        }
        return PowerStatusInfo.FromNative(in native);
    }

    private void OnPowerBroadcast(PowerEventKind kind, nint rawCode, int? settingValue)
    {
        // 窗口过程运行在 UI 线程，此处只做快速取数与事件分发，不做耗时操作。
        // 显示器/合盖事件本身不改变 GetSystemPowerStatus 的语义，但附带快照可便于日志关联。
        PowerStatusInfo? snapshot = null;
        try
        {
            snapshot = GetCurrentStatus();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"获取电源状态快照失败：{ex}");
        }

        var args = new PowerStateChangedEventArgs
        {
            Kind = kind,
            RawCode = rawCode,
            SettingValue = settingValue,
            Status = snapshot,
            Timestamp = DateTimeOffset.Now
        };

        try
        {
            PowerModeChanged?.Invoke(this, args);

            if (kind == PowerEventKind.PowerStatusChange && snapshot is not null)
            {
                PowerStatusChanged?.Invoke(this, snapshot);
            }
        }
        catch (Exception ex)
        {
            // 订阅者异常绝不能逃逸到窗口过程，否则可能导致消息循环崩溃。
            AppLog.Log($"分发电源事件 {kind} 时订阅者抛出异常", ex);
        }
    }

    /// <summary>将 <see cref="PowerEventKind"/> 转换为中文说明。</summary>
    public static string DescribeKind(PowerEventKind kind) => kind switch
    {
        PowerEventKind.QuerySuspend => "系统请求挂起许可",
        PowerEventKind.QuerySuspendFailed => "系统挂起请求被拒绝",
        PowerEventKind.Suspend => "系统即将进入睡眠/休眠",
        PowerEventKind.ResumeCritical => "系统从严重挂起中恢复",
        PowerEventKind.ResumeSuspend => "系统已从睡眠中恢复（用户唤醒）",
        PowerEventKind.ResumeAutomatic => "系统已自动唤醒",
        PowerEventKind.BatteryLow => "电池电量低警告",
        PowerEventKind.PowerStatusChange => "电源状态发生变化",
        PowerEventKind.DisplayStateChanged => "显示器状态变化",
        PowerEventKind.LidSwitchStateChanged => "合盖开关动作",
        PowerEventKind.AdapterConnected => "电源适配器状态变化",
        _ => "未知电源事件"
    };

    /// <summary>
    /// 将电源设置类事件的数值转换为中文说明。
    /// 显示器：0=关闭，1=开启，2=变暗；合盖：0=合上，1=打开。
    /// </summary>
    public static string DescribeSetting(PowerEventKind kind, int value) => kind switch
    {
        PowerEventKind.DisplayStateChanged => value switch
        {
            0 => "显示器已关闭",
            1 => "显示器已开启",
            2 => "显示器已变暗",
            _ => $"未知显示器状态（{value}）"
        },
        PowerEventKind.LidSwitchStateChanged => value switch
        {
            0 => "已合上盖子",
            1 => "已打开盖子",
            _ => $"未知合盖状态（{value}）"
        },
        PowerEventKind.AdapterConnected => value switch
        {
            0 => "适配器已插入（交流电）",
            1 => "适配器已拔出（切换至电池供电）",
            2 => "热插拔电源",
            _ => $"未知电源来源（{value}）"
        },
        _ => value.ToString()
    };

    private void DestroyWindow()
    {
        if (_window is null) return;
        _window.PowerBroadcast -= OnPowerBroadcast;
        _window.Dispose();
        _window = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            DestroyWindow();
            _disposed = true;
        }
    }

    /// <summary>
    /// 仅用于接收系统消息的隐藏原生窗口。
    /// </summary>
    private sealed class PowerMessageWindow : NativeWindow, IDisposable
    {
        private const int WmPowerBroadcast = 0x0218;
        private const int PbtPowerSettingChange = 0x8013;

        private const int PbtApmQuerySuspend = 0x0000;
        private const int PbtApmQuerySuspendFailed = 0x0002;
        private const int PbtApmSuspend = 0x0004;
        private const int PbtApmResumeCritical = 0x0006;
        private const int PbtApmResumeSuspend = 0x0007;
        private const int PbtApmBatteryLow = 0x0009;
        private const int PbtApmPowerStatusChange = 0x000A;
        private const int PbtApmResumeAutomatic = 0x0012;

        private const uint DeviceNotifyWindowHandle = 0x00000000;

        // GUID_CONSOLE_DISPLAY_STATE：主显示器 开启/关闭/变暗
        private static readonly Guid GuidConsoleDisplayState =
            new("6FE69556-7E4A-47A0-893D-9CB3E4AE5312");

        // GUID_LIDSWITCH_STATE_CHANGE：笔记本盖子 打开/合上
        private static readonly Guid GuidLidswitchStateChange =
            new("BA3E0F4D-B817-4094-A2D1-D56379E6A0F8");

        // GUID_ACDC_POWER_SOURCE：电源适配器插拔（0=交流电，1=直流电/电池，2=热插拔）
        private static readonly Guid GuidAcdcPowerSource =
            new("5D3E42DE-8935-46E6-94D4-03B3D71C1FE6");

        private readonly List<nint> _notificationHandles = [];

        public event Action<PowerEventKind, nint, int?>? PowerBroadcast;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern nint RegisterPowerSettingNotification(
            nint hRecipient, ref Guid powerSettingGuid, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterPowerSettingNotification(nint handle);

        public PowerMessageWindow()
        {
            // 创建一个不可见的顶层消息窗口（不指定 WS_VISIBLE，永不 Show）。
            var cp = new CreateParams
            {
                Caption = "PowerMonitorMessageWindow"
            };
            CreateHandle(cp);

            // 句柄创建成功后订阅细粒度电源设置；注册失败时抛出带 Win32 错误的异常，避免静默失效。
            RegisterNotification(GuidConsoleDisplayState, nameof(GuidConsoleDisplayState));
            RegisterNotification(GuidLidswitchStateChange, nameof(GuidLidswitchStateChange));
            RegisterNotification(GuidAcdcPowerSource, nameof(GuidAcdcPowerSource));
        }

        private void RegisterNotification(Guid setting, string name)
        {
            var guid = setting;
            var handle = RegisterPowerSettingNotification(Handle, ref guid, DeviceNotifyWindowHandle);
            if (handle == 0)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"注册电源通知失败：{name}（{setting:B}）");
            }
            _notificationHandles.Add(handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmPowerBroadcast)
            {
                if (m.WParam == PbtPowerSettingChange)
                {
                    HandlePowerSettingChange(m.LParam);
                }
                else
                {
                    var code = m.WParam.ToInt64();
                    var kind = code switch
                    {
                        PbtApmQuerySuspend => PowerEventKind.QuerySuspend,
                        PbtApmQuerySuspendFailed => PowerEventKind.QuerySuspendFailed,
                        PbtApmSuspend => PowerEventKind.Suspend,
                        PbtApmResumeCritical => PowerEventKind.ResumeCritical,
                        PbtApmResumeSuspend => PowerEventKind.ResumeSuspend,
                        PbtApmBatteryLow => PowerEventKind.BatteryLow,
                        PbtApmPowerStatusChange => PowerEventKind.PowerStatusChange,
                        PbtApmResumeAutomatic => PowerEventKind.ResumeAutomatic,
                        _ => PowerEventKind.Unknown
                    };
                    AppLog.Log($"窗口过程收到 WM_POWERBROADCAST：{kind} (0x{code:X4})");
                    try
                    {
                        PowerBroadcast?.Invoke(kind, m.WParam, null);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Log($"窗口过程处理 {kind} 时异常", ex);
                    }
                }
            }

            base.WndProc(ref m);
        }

        private void HandlePowerSettingChange(nint lParam)
        {
            if (lParam == 0) return;

            var setting = Marshal.PtrToStructure<PowerBroadcastSetting>(lParam);
            if (setting.DataLength < sizeof(int)) return;

            // Data 字段为内联缓冲，DWORD 值从 Data 的偏移处读取（4 字节）。
            var dataOffset = (int)Marshal.OffsetOf<PowerBroadcastSetting>(nameof(PowerBroadcastSetting.Data));
            var value = Marshal.ReadInt32(lParam, dataOffset);

            var kind = setting.PowerSetting == GuidConsoleDisplayState
                ? PowerEventKind.DisplayStateChanged
                : setting.PowerSetting == GuidLidswitchStateChange
                    ? PowerEventKind.LidSwitchStateChanged
                    : setting.PowerSetting == GuidAcdcPowerSource
                        ? PowerEventKind.AdapterConnected
                        : PowerEventKind.Unknown;

            AppLog.Log($"窗口过程收到 PBT_POWERSETTINGCHANGE：{kind}，value={value}");
            try
            {
                PowerBroadcast?.Invoke(kind, PbtPowerSettingChange, value);
            }
            catch (Exception ex)
            {
                AppLog.Log($"窗口过程处理 {kind} 时异常", ex);
            }
        }

        public void Dispose()
        {
            AppLog.Log("PowerMessageWindow.Dispose 被调用，开始注销通知并销毁窗口");
            foreach (var handle in _notificationHandles)
            {
                UnregisterPowerSettingNotification(handle);
            }
            _notificationHandles.Clear();

            if (Handle != 0)
            {
                DestroyHandle();
            }
        }

        public override void DestroyHandle()
        {
            AppLog.Log("PowerMessageWindow.DestroyHandle 被调用");
            base.DestroyHandle();
        }
    }

    /// <summary>PBT_POWERSETTINGCHANGE 的 lParam：POWERBROADCAST_SETTING 结构。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PowerBroadcastSetting
    {
        public Guid PowerSetting;
        public uint DataLength;

        /// <summary>内联数据缓冲首字节；实际值按 DataLength 解释（此处均为 DWORD）。</summary>
        public byte Data;
    }
}
