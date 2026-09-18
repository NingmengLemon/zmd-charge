using System;
using System.Management;
using System.Runtime.Versioning;

namespace EndfieldCharge.Services;

/// <summary>一次电池采样结果。</summary>
public sealed record BatterySnapshot(
    double RemainingWh,
    double FullWh,
    int Percent,
    bool AcOnline,
    bool Charging)
{
    public bool HasBattery => FullWh > 0;
}

/// <summary>
/// 电池信息读取。主路径走 powrprof（快、准、同步），失败时退回 WMI Win32_Battery。
/// </summary>
[SupportedOSPlatform("windows")]
public static class BatteryService
{
    /// <summary>取当前电池快照；无电池或读取失败返回 null。</summary>
    public static BatterySnapshot? GetSnapshot()
    {
        if (TryFromPowerProf(out var snap))
            return snap;

        return TryFromWmi();
    }

    /// <summary>
    /// 剩余百分比。用容量比而不是 EstimatedChargeRemaining，后者常为整数跳变。
    /// 抽出来是为了能脱离 Windows API 单测（见 tests/EndfieldCharge.Tests）。
    /// </summary>
    internal static int ComputePercent(double remainingWh, double fullWh)
    {
        if (fullWh <= 0)
            return 0;

        return Math.Clamp((int)Math.Round(remainingWh / fullWh * 100.0), 0, 100);
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
            Percent: ComputePercent(remainingWh, fullWh),
            // powrprof 的 Charging 语义是"正在充"（接电但已充满/未充时为 0），
            // 与 AcOnline 严格区分；提醒逻辑依赖这个区别。
            AcOnline: s.AcOnLine != 0,
            Charging: s.Charging != 0);
        return true;
    }

    // Win32_Battery.BatteryStatus 取值（WMI 文档）：
    //   1 放电 | 2 接 AC 但不一定在充 | 3 已充满 | 4 低 | 5 危急
    //   6 充电中 | 7 充电且高 | 8 充电且低 | 9 充电且危急 | 10 未定义 | 11 部分充电
    // internal 以便单测：status=2/3/11 是"接了电但没在充"，不能被当成 Charging。
    internal static bool IsAcOnline(ushort status) => status is 2 or 3 or 6 or 7 or 8 or 9 or 11;
    internal static bool IsCharging(ushort status) => status is 6 or 7 or 8 or 9;

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
                    AcOnline: IsAcOnline(status),
                    Charging: IsCharging(status));
            }
        }
        catch
        {
            // WMI 被禁用或服务未启动时静默失败
        }

        return null;

        static ushort? ReadUInt16(object? v) => v is null ? null : Convert.ToUInt16(v);
        static uint? ReadUInt32(object? v) => v is null ? null : Convert.ToUInt32(v);
        static uint? NonZero(uint? v) => v is > 0 ? v : null;
    }
}
