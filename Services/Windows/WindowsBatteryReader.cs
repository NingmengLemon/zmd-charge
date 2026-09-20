using System.Globalization;
using System.Management;
using System.Runtime.Versioning;

namespace EndfieldCharge.Services.Windows;

/// <summary>
/// Windows 电池读取：powrprof 主路径（快、准、同步），失败退回 WMI Win32_Battery。
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsBatteryReader
{
    /// <summary>取当前电池快照；无电池或读取失败返回 null。</summary>
    internal static BatterySnapshot? Read()
    {
        if (TryFromPowerProf(out var snap))
            return snap;

        return TryFromWmi();
    }

    private static bool TryFromPowerProf(out BatterySnapshot? snapshot)
    {
        snapshot = null;
        if (!PowerNative.TryGetBatteryState(out var s))
            return false;

        // 有的固件 MaxCapacity 给的是"设计容量"而非"当前满充容量"，这里只做合理性校验
        if (s.MaxCapacity == 0)
            return false;

        double fullWh = s.MaxCapacity / 1000.0;
        double remainingWh = s.RemainingCapacity / 1000.0;

        snapshot = new BatterySnapshot(
            RemainingWh: remainingWh,
            FullWh: fullWh,
            Percent: BatteryService.ComputePercent(remainingWh, fullWh),
            // powrprof 的 Charging 语义是"正在充"（接电但已充满/未充时为 0），
            // 与 AcOnline 严格区分；提醒逻辑依赖这个区别。
            AcOnline: s.AcOnLine != 0,
            Charging: s.Charging != 0);
        return true;
    }

    // Win32_Battery.BatteryStatus 的语义映射见 Services/WmiBatteryStatus.cs
    // （放在平台无关处，便于在任何平台单测）

    private static BatterySnapshot? TryFromWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "root\\CIMV2",
                "SELECT EstimatedChargeRemaining, FullChargeCapacity, DesignCapacity, BatteryStatus FROM Win32_Battery");

            foreach (ManagementObject mo in searcher.Get())
            {
                int? pct = ReadUInt16(mo["EstimatedChargeRemaining"]);
                // 0 与"读不到"等价：部分机型 FullChargeCapacity 存在但为 0，
                // 必须先归一成 null，否则 ?? 不会回退到 DesignCapacity。
                uint? fullMwh = NonZero(ReadUInt32(mo["FullChargeCapacity"]))
                                ?? NonZero(ReadUInt32(mo["DesignCapacity"]));

                if (pct is null || fullMwh is null)
                    continue;

                double fullWh = fullMwh.Value / 1000.0;
                double remainingWh = fullWh * pct.Value / 100.0;

                ushort status = ReadUInt16(mo["BatteryStatus"]) ?? 0;

                return new BatterySnapshot(
                    RemainingWh: remainingWh,
                    FullWh: fullWh,
                    Percent: Math.Clamp(pct.Value, 0, 100),
                    AcOnline: WmiBatteryStatus.IsAcOnline(status),
                    Charging: WmiBatteryStatus.IsCharging(status));
            }
        }
        catch
        {
            // WMI 被禁用或服务未启动时静默失败
        }

        return null;

        // WMI 返回的是 VARIANT，值可能是字符串，转换必须钉死不变文化：
        // 某些区域设置下数字的解析结果会随 CurrentCulture 变（CA1305）
        static ushort? ReadUInt16(object? v) => v is null ? null : Convert.ToUInt16(v, CultureInfo.InvariantCulture);
        static uint? ReadUInt32(object? v) => v is null ? null : Convert.ToUInt32(v, CultureInfo.InvariantCulture);
        static uint? NonZero(uint? v) => v is > 0 ? v : null;
    }
}
