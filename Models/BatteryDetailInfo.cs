namespace PowerMonitor.Models;

/// <summary>
/// 电池硬件详情：通过 WMI 查询的设计容量、满充容量、循环次数、制造商、化学类型等。
/// 用于在适配器拔出时展示电池健康状态，并检测系统自带的电池。
/// </summary>
public sealed record BatteryDetailInfo
{
    /// <summary>设备实例名（如 ACPI\PNP0C0A\0_0）。</summary>
    public required string InstanceName { get; init; }

    /// <summary>设计容量（mWh）。</summary>
    public uint? DesignedCapacity { get; init; }

    /// <summary>当前满充容量（mWh），反映电池老化程度。</summary>
    public uint? FullChargedCapacity { get; init; }

    /// <summary>循环次数。</summary>
    public uint? CycleCount { get; init; }

    /// <summary>制造商名称。</summary>
    public string? Manufacturer { get; init; }

    /// <summary>序列号。</summary>
    public string? SerialNumber { get; init; }

    /// <summary>化学类型（1=锂离子，2=镍镉，3=镍氢，4=铅酸，5=锂聚合物等）。</summary>
    public byte? Chemistry { get; init; }

    /// <summary>制造日期（仅 WMI BatteryStaticData 可用时）。</summary>
    public string? ManufactureDate { get; init; }

    /// <summary>电池健康度百分比：满充容量 / 设计容量 * 100。</summary>
    public double? HealthPercent =>
        DesignedCapacity is > 0 and var d && FullChargedCapacity is > 0 and var f
            ? Math.Round(100.0 * f / d, 1)
            : null;

    public string HealthText => HealthPercent switch
    {
        >= 80 => "良好",
        >= 60 => "一般",
        > 0 => "需更换",
        _ => "未知"
    };

    public string ChemistryText => Chemistry switch
    {
        1 => "锂离子",
        2 => "镍镉",
        3 => "镍氢",
        4 => "铅酸",
        5 => "锂聚合物",
        _ => Chemistry?.ToString() ?? "未知"
    };

    /// <summary>生成电池详情摘要文本。</summary>
    public string ToSummary()
    {
        var parts = new List<string>();
        if (DesignedCapacity is > 0)
            parts.Add($"设计 {DesignedCapacity} mWh");
        if (FullChargedCapacity is > 0)
            parts.Add($"满充 {FullChargedCapacity} mWh");
        if (HealthPercent is > 0)
            parts.Add($"健康度 {HealthPercent}%");
        if (CycleCount is > 0)
            parts.Add($"循环 {CycleCount} 次");
        if (!string.IsNullOrEmpty(Manufacturer))
            parts.Add(Manufacturer);
        parts.Add(ChemistryText);
        return parts.Count > 0 ? string.Join("，", parts) : "无详细信息";
    }
}
