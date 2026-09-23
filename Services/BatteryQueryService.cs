using System.Diagnostics;
using System.Management;
using PowerMonitor.Models;

namespace PowerMonitor.Services;

/// <summary>
/// 电池硬件查询服务：通过 WMI 检测系统自带的电池，查询设计容量、满充容量、循环次数等。
/// 在适配器拔出时调用，展示电池健康状态。
/// </summary>
public sealed class BatteryQueryService
{
    /// <summary>
    /// 查询所有电池的详细硬件信息。
    /// </summary>
    public List<BatteryDetailInfo> QueryAllBatteries()
    {
        var results = new List<BatteryDetailInfo>();
        try
        {
            QueryStaticData(results);
        }
        catch (Exception ex)
        {
            AppLog.Log("查询电池静态数据失败", ex);
        }
        return results;
    }

    private static void QueryStaticData(List<BatteryDetailInfo> results)
    {
        using var searcher = new ManagementObjectSearcher(
            @"\\.\root\WMI", "SELECT * FROM BatteryStaticData");
        foreach (var obj in searcher.Get().Cast<ManagementObject>())
        {
            var info = new BatteryDetailInfo
            {
                InstanceName = TryGetString(obj, "InstanceName") ?? "Unknown",
                DesignedCapacity = TryGetUint(obj, "DesignedCapacity"),
                ManufactureDate = TryGetString(obj, "ManufactureDate"),
                Chemistry = TryGetByte(obj, "Chemistry"),
                Manufacturer = TryGetString(obj, "Manufacturer"),
                SerialNumber = TryGetString(obj, "SerialNumber"),
                FullChargedCapacity = QueryFullChargedCapacity(
                    TryGetString(obj, "InstanceName")),
                CycleCount = QueryCycleCount(
                    TryGetString(obj, "InstanceName"))
            };
            results.Add(info);
            AppLog.Log($"检测到电池：{info.InstanceName}，{info.ToSummary()}");
        }
    }

    /// <summary>查询电池的当前满充容量。</summary>
    private static uint? QueryFullChargedCapacity(string? instanceName)
    {
        if (string.IsNullOrEmpty(instanceName)) return null;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"\\.\root\WMI",
                "SELECT * FROM BatteryFullChargedCapacity");
            foreach (var obj in searcher.Get().Cast<ManagementObject>())
            {
                if (string.Equals(TryGetString(obj, "InstanceName"), instanceName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return TryGetUint(obj, "FullChargedCapacity");
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>查询电池循环次数（BatteryCycleCount）。</summary>
    private static uint? QueryCycleCount(string? instanceName)
    {
        if (string.IsNullOrEmpty(instanceName)) return null;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"\\.\root\WMI",
                "SELECT * FROM BatteryCycleCount");
            foreach (var obj in searcher.Get().Cast<ManagementObject>())
            {
                if (string.Equals(TryGetString(obj, "InstanceName"), instanceName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return TryGetUint(obj, "CycleCount");
                }
            }
        }
        catch { }
        return null;
    }

    private static string? TryGetString(ManagementBaseObject obj, string name)
    {
        try { return obj[name]?.ToString(); } catch { return null; }
    }

    private static uint? TryGetUint(ManagementBaseObject obj, string name)
    {
        try { return obj[name] is uint v ? v : null; } catch { return null; }
    }

    private static byte? TryGetByte(ManagementBaseObject obj, string name)
    {
        try { return obj[name] is byte v ? v : null; } catch { return null; }
    }
}
