namespace PowerMonitor.Models;

/// <summary>
/// Windows 电源广播事件类型（对应 Win32 WM_POWERBROADCAST 的 wParam 取值）。
/// </summary>
public enum PowerEventKind
{
    /// <summary>PBT_APMQUERYSUSPEND (0x0000)：系统请求挂起许可。</summary>
    QuerySuspend,

    /// <summary>PBT_APMQUERYSUSPENDFAILED (0x0002)：挂起请求被拒绝。</summary>
    QuerySuspendFailed,

    /// <summary>PBT_APMSUSPEND (0x0004)：系统即将进入睡眠/休眠。</summary>
    Suspend,

    /// <summary>PBT_APMRESUMECRITICAL (0x0006)：从严重挂起中恢复。</summary>
    ResumeCritical,

    /// <summary>PBT_APMRESUMESUSPEND (0x0007)：用户操作使系统从睡眠中恢复。</summary>
    ResumeSuspend,

    /// <summary>PBT_APMBATTERYLOW (0x0009)：电池电量低警告。</summary>
    BatteryLow,

    /// <summary>PBT_APMPOWERSTATUSCHANGE (0x000A)：供电/电池状态变化（交流切换、电量变化等）。</summary>
    PowerStatusChange,

    /// <summary>PBT_APMRESUMEAUTOMATIC (0x0012)：系统自动唤醒（如唤醒定时器、合盖唤醒）。</summary>
    ResumeAutomatic,

    /// <summary>
    /// PBT_POWERSETTINGCHANGE — GUID_CONSOLE_DISPLAY_STATE：显示器状态变化（开启/关闭/变暗）。
    /// 需事先通过 RegisterPowerSettingNotification 订阅。
    /// </summary>
    DisplayStateChanged,

    /// <summary>
    /// PBT_POWERSETTINGCHANGE — GUID_LIDSWITCH_STATE_CHANGE：笔记本合盖开关变化（打开/合上）。
    /// </summary>
    LidSwitchStateChanged,

    /// <summary>
    /// PBT_POWERSETTINGCHANGE — GUID_ACDC_POWER_SOURCE：电源适配器插拔事件。
    /// 值：0=交流电（适配器插入），1=直流电（适配器拔出，使用电池），2=热插拔电源。
    /// </summary>
    AdapterConnected,

    /// <summary>未识别的电源广播消息。</summary>
    Unknown
}
