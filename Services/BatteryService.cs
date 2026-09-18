using EndfieldCharge.Services.Linux;
using EndfieldCharge.Services.Windows;

namespace EndfieldCharge.Services;

/// <summary>一次电池采样结果。</summary>
/// <param name="RemainingWh">剩余容量（Wh）。容量未知时为 0。</param>
/// <param name="FullWh">满充容量（Wh）。容量未知时为 0。</param>
/// <param name="Percent">剩余百分比 0..100。</param>
/// <param name="AcOnline">是否接着交流电（接了电但没在充也算）。</param>
/// <param name="Charging">是否正在充电。与 AcOnline 严格区分，提醒逻辑依赖这个区别。</param>
/// <param name="HasBattery">
/// 是否检测到电池设备。刻意与 FullWh 是否为 0 分开：
/// 少数 Linux 驱动只提供 capacity / status，能给出电量百分比却给不出容量，
/// 那种情况下仍然应该弹低电量提醒，只是 Wh 显示不出来。
/// </param>
public sealed record BatterySnapshot(
    double RemainingWh,
    double FullWh,
    int Percent,
    bool AcOnline,
    bool Charging,
    bool HasBattery = true);

/// <summary>
/// 电池信息读取的门面。按操作系统分派到具体实现，调用方不需要关心平台：
///   Windows：powrprof 主路径，失败退回 WMI Win32_Battery
///   Linux  ：直接读 sysfs（/sys/class/power_supply），不依赖 UPower 之类的守护进程
/// </summary>
public static class BatteryService
{
    /// <summary>取当前电池快照；无电池或读取失败返回 null。</summary>
    public static BatterySnapshot? GetSnapshot()
    {
        if (OperatingSystem.IsWindows())
            return WindowsBatteryReader.Read();

        if (OperatingSystem.IsLinux())
            return LinuxBatteryReader.ReadDefault();

        return null;
    }

    /// <summary>
    /// 剩余百分比。用容量比而不是平台自报的整数百分比，后者常为整数跳变。
    /// 抽出来是为了能脱离平台 API 单测（见 tests/EndfieldCharge.Tests）。
    /// </summary>
    internal static int ComputePercent(double remainingWh, double fullWh)
    {
        if (fullWh <= 0)
            return 0;

        return Math.Clamp((int)Math.Round(remainingWh / fullWh * 100.0), 0, 100);
    }
}
