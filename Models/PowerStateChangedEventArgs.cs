using PowerMonitor.Services;

namespace PowerMonitor.Models;

/// <summary>
/// 电源广播事件参数：系统在睡眠/恢复/供电切换/低电量等时刻触发。
/// </summary>
public sealed class PowerStateChangedEventArgs : EventArgs
{
    public required PowerEventKind Kind { get; init; }

    /// <summary>原始 WM_POWERBROADCAST wParam 值，便于诊断未识别事件。</summary>
    public required nint RawCode { get; init; }

    /// <summary>事件发生时刻的最新电源状态快照（获取失败时为 null）。</summary>
    public PowerStatusInfo? Status { get; init; }

    /// <summary>
    /// 电源设置类事件的新值（仅 DisplayStateChanged / LidSwitchStateChanged 有值）：
    /// 显示器：0=关闭，1=开启，2=变暗；合盖：0=合上，1=打开。
    /// </summary>
    public int? SettingValue { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>事件的中文说明。</summary>
    public string KindText => PowerStateService.DescribeKind(Kind);
}
