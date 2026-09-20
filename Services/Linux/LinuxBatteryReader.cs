using System.Globalization;

namespace EndfieldCharge.Services.Linux;

/// <summary>
/// Linux 电池读取：直接读 sysfs，不依赖 UPower 之类的守护进程。
///
/// 根路径可注入，这是刻意的：CI runner 与虚拟机都没有电池，
/// 不注入就只能靠真机手测，这条路径等于没有自动验证。注入之后可以用 fixture 目录单测。
/// 也正因为只用到 System.IO，这个类本身不标 [SupportedOSPlatform("linux")]，
/// 在 Windows 上也能拿 fixture 跑测试。
///
/// sysfs 取值约定（kernel 文档 ABI/testing/sysfs-class-power）：
///   type         Battery / Mains / UPS / USB
///   capacity     0..100，内核自己算好的整数百分比
///   status       Unknown / Charging / Discharging / Not charging / Full
///   energy_now, energy_full   微瓦时（µWh）
///   charge_now, charge_full   微安时（µAh），要配合 voltage_now（µV）才能算成 Wh
///   online       1 / 0，只有 Mains 设备有
/// </summary>
internal sealed class LinuxBatteryReader
{
    internal const string DefaultRoot = "/sys/class/power_supply";

    private readonly string _root;

    internal LinuxBatteryReader(string root) => _root = root;

    internal static BatterySnapshot? ReadDefault() => new LinuxBatteryReader(DefaultRoot).GetSnapshot();

    /// <summary>取当前电池快照；没有电池设备或读不到返回 null。</summary>
    internal BatterySnapshot? GetSnapshot()
    {
        string[] devices;
        try
        {
            if (!Directory.Exists(_root))
                return null;

            devices = Directory.GetDirectories(_root);
        }
        catch
        {
            return null;
        }

        double totalRemainingWh = 0;
        double totalFullWh = 0;
        var capacities = new List<int>();
        var statuses = new List<string>();
        bool foundBattery = false;

        foreach (var dir in devices)
        {
            if (!IsType(dir, "Battery"))
                continue;

            foundBattery = true;

            if (ReadInt(dir, "capacity") is int cap)
                capacities.Add(Math.Clamp(cap, 0, 100));

            if (ReadText(dir, "status") is { Length: > 0 } status)
                statuses.Add(status);

            // 多电池机型（BAT0 + BAT1）按容量累加
            if (TryReadWh(dir, out double remainingWh, out double fullWh))
            {
                totalRemainingWh += remainingWh;
                totalFullWh += fullWh;
            }
        }

        if (!foundBattery)
            return null;

        // 百分比优先按容量比算（与 Windows 侧一致，避开内核整数跳变）；
        // 拿不到容量时退回 capacity 的平均值。
        int percent = totalFullWh > 0
            ? BatteryService.ComputePercent(totalRemainingWh, totalFullWh)
            : capacities.Count > 0
                ? (int)Math.Round(capacities.Average(), MidpointRounding.AwayFromZero)
                : 0;

        bool charging = statuses.Contains("Charging", StringComparer.Ordinal);

        return new BatterySnapshot(
            RemainingWh: totalRemainingWh,
            FullWh: totalFullWh,
            Percent: percent,
            // 优先信 Mains 设备的 online；没有 Mains 设备时从电池状态反推
            AcOnline: ReadAcOnline(devices) ?? AcFromStatus(statuses),
            Charging: charging,
            // 找到电池设备就算有电池，与"容量知不知道"无关：
            // 少数驱动只给 capacity/status，那时 FullWh 为 0，但电量百分比仍然有效。
            HasBattery: true);
    }

    /// <summary>有没有接着电。Charging / Full / Not charging 都意味着接着电，
    /// 与 Windows 侧 WMI 的语义对齐（接了电但没在充也算 AC）。</summary>
    private static bool AcFromStatus(List<string> statuses) =>
        statuses.Exists(s => s is "Charging" or "Full" or "Not charging");

    private static bool? ReadAcOnline(string[] devices)
    {
        bool foundMains = false;

        foreach (var dir in devices)
        {
            if (!IsType(dir, "Mains"))
                continue;

            if (ReadInt(dir, "online") is not int online)
                continue;

            foundMains = true;
            if (online != 0)
                return true;
        }

        return foundMains ? false : null;
    }

    /// <summary>优先用 energy_*（µWh）；没有就退回 charge_*（µAh）× voltage_now（µV）。</summary>
    private static bool TryReadWh(string dir, out double remainingWh, out double fullWh)
    {
        remainingWh = 0;
        fullWh = 0;

        if (ReadLong(dir, "energy_now") is long energyNow &&
            ReadLong(dir, "energy_full") is long energyFull)
        {
            remainingWh = energyNow / 1_000_000.0;
            fullWh = energyFull / 1_000_000.0;
            return true;
        }

        if (ReadLong(dir, "charge_now") is long chargeNow &&
            ReadLong(dir, "charge_full") is long chargeFull &&
            ReadLong(dir, "voltage_now") is long microVolt)
        {
            double volt = microVolt / 1_000_000.0;
            remainingWh = chargeNow / 1_000_000.0 * volt;
            fullWh = chargeFull / 1_000_000.0 * volt;
            return true;
        }

        return false;
    }

    private static bool IsType(string dir, string expected) =>
        string.Equals(ReadText(dir, "type"), expected, StringComparison.Ordinal);

    /// <summary>读一个 sysfs 属性文件。设备可能随时被拔掉，读取失败一律当成"读不到"。</summary>
    private static string? ReadText(string dir, string name)
    {
        try
        {
            var path = Path.Combine(dir, name);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static int? ReadInt(string dir, string name) =>
        int.TryParse(ReadText(dir, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;

    private static long? ReadLong(string dir, string name) =>
        long.TryParse(ReadText(dir, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out long v) ? v : null;
}
