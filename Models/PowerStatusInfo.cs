using System.Runtime.InteropServices;

namespace PowerMonitor.Models;

/// <summary>
/// 电源状态快照，对应 Win32 API <c>GetSystemPowerStatus</c> 返回的信息。
/// </summary>
public sealed record PowerStatusInfo
{
    /// <summary>交流电源状态：0=使用电池，1=接通交流电，255=未知。</summary>
    public required byte AcLineStatus { get; init; }

    /// <summary>电池状态标志位（充电中/低电量/严重低电量/未安装电池等的位组合）。</summary>
    public required byte BatteryFlag { get; init; }

    /// <summary>剩余电量百分比（0~100），255 表示未知。</summary>
    public required byte BatteryLifePercent { get; init; }

    /// <summary>系统状态标志：非 0 表示节电模式已开启。</summary>
    public required byte SystemStatusFlag { get; init; }

    /// <summary>电池剩余寿命（秒），0xFFFFFFFF 表示未知。</summary>
    public required uint BatteryLifeTime { get; init; }

    /// <summary>电池满电寿命（秒），0xFFFFFFFF 表示未知。</summary>
    public required uint BatteryFullLifeTime { get; init; }

    /// <summary>获取本次快照的时间。</summary>
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;

    public bool IsAcPower => AcLineStatus == 1;
    public bool IsBatteryPower => AcLineStatus == 0;
    public bool PowerSourceUnknown => AcLineStatus is not (0 or 1);

    public bool HasBattery => (BatteryFlag & 0x80) == 0;
    public bool IsCharging => (BatteryFlag & 0x08) != 0;
    public bool IsCritical => (BatteryFlag & 0x04) != 0;
    public bool IsLow => (BatteryFlag & 0x02) != 0;
    public bool IsHigh => (BatteryFlag & 0x01) != 0;
    public bool PercentUnknown => BatteryLifePercent == 255;
    public bool BatterySaverOn => SystemStatusFlag != 0;

    public string PowerSourceText => AcLineStatus switch
    {
        0 => "电池供电",
        1 => "交流电",
        _ => "未知"
    };

    public string BatteryStateText
    {
        get
        {
            if (!HasBattery) return "未安装电池";
            if (IsCharging) return "充电中";
            if (IsCritical) return "电量严重不足";
            if (IsLow) return "电量低";
            if (IsHigh) return "电量充足";
            return "正常";
        }
    }

    public string PercentText => PercentUnknown ? "未知" : $"{BatteryLifePercent}%";

    public string RemainingTimeText =>
        BatteryLifeTime is 0 or 0xFFFFFFFF
            ? "未知"
            : TimeSpan.FromSeconds(BatteryLifeTime).ToString(@"hh\:mm\:ss");

    /// <summary>生成一行用于日志/通知的摘要文本。</summary>
    public string ToSummary()
    {
        if (!HasBattery) return PowerSourceText;
        var charging = IsCharging ? "（充电中）" : string.Empty;
        return $"{PowerSourceText}，{PercentText}{charging}，剩余 {RemainingTimeText}";
    }

    /// <summary>
    /// 从非托管 <see cref="SYSTEM_POWER_STATUS"/> 结构转换为托管快照。
    /// </summary>
    internal static PowerStatusInfo FromNative(in SYSTEM_POWER_STATUS s) => new()
    {
        AcLineStatus = s.ACLineStatus,
        BatteryFlag = s.BatteryFlag,
        BatteryLifePercent = s.BatteryLifePercent,
        SystemStatusFlag = s.SystemStatusFlag,
        BatteryLifeTime = s.BatteryLifeTime,
        BatteryFullLifeTime = s.BatteryFullLifeTime
    };
}

/// <summary>与 kernel32!GetSystemPowerStatus 对应的非托管结构。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEM_POWER_STATUS
{
    public byte ACLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public uint BatteryLifeTime;
    public uint BatteryFullLifeTime;
}
