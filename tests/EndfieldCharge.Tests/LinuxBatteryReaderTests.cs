using System.Globalization;
using EndfieldCharge.Services.Linux;
using Xunit;

namespace EndfieldCharge.Tests;

/// <summary>
/// Linux 电池读取的单测。
///
/// 关键点：这条路径平时根本测不到 —— CI runner 与虚拟机都没有电池。
/// 所以 LinuxBatteryReader 的 sysfs 根路径是可注入的，这里用 fixture 目录喂它，
/// 在 Windows 上也能跑（这个类只用到 System.IO）。
/// </summary>
public class LinuxBatteryReaderTests
{
    /// <summary>搭一棵假的 sysfs 树。写法对齐真实 sysfs：每个属性文件都以换行结尾。</summary>
    private sealed class SysfsFixture : IDisposable
    {
        private readonly string _root;

        public SysfsFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), "endfieldcharge-sysfs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public SysfsFixture Battery(
            string name = "BAT0",
            string status = "Discharging",
            int? capacity = null,
            long? energyNow = null,
            long? energyFull = null,
            long? chargeNow = null,
            long? chargeFull = null,
            long? voltageNow = null)
        {
            var dir = MakeDevice(name, "Battery");
            Write(dir, "status", status);
            WriteIf(dir, "capacity", capacity);
            WriteIf(dir, "energy_now", energyNow);
            WriteIf(dir, "energy_full", energyFull);
            WriteIf(dir, "charge_now", chargeNow);
            WriteIf(dir, "charge_full", chargeFull);
            WriteIf(dir, "voltage_now", voltageNow);
            return this;
        }

        public SysfsFixture Mains(string name = "ACAD", int online = 1)
        {
            WriteIf(MakeDevice(name, "Mains"), "online", online);
            return this;
        }

        /// <summary>造一个不是电池也不是交流电的设备（UPS / USB），用来验证会被跳过。</summary>
        public SysfsFixture Other(string name, string type)
        {
            WriteIf(MakeDevice(name, type), "capacity", 50);
            return this;
        }

        public LinuxBatteryReader Reader => new(_root);

        private string MakeDevice(string name, string type)
        {
            var dir = Path.Combine(_root, name);
            Directory.CreateDirectory(dir);
            Write(dir, "type", type);
            return dir;
        }

        private static void Write(string dir, string name, string value) =>
            File.WriteAllText(Path.Combine(dir, name), value + "\n");

        private static void WriteIf(string dir, string name, long? value)
        {
            if (value is not null)
                Write(dir, name, value.Value.ToString(CultureInfo.InvariantCulture));
        }

        private static void WriteIf(string dir, string name, int? value)
        {
            if (value is not null)
                Write(dir, name, value.Value.ToString(CultureInfo.InvariantCulture));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // 清理失败不影响用例结论
            }
        }
    }

    [Fact]
    public void 有电池无交流电设备时从状态反推在放电()
    {
        using var fs = new SysfsFixture().Battery(
            status: "Discharging", energyNow: 58_590_000, energyFull: 74_160_000);

        var snap = fs.Reader.GetSnapshot();

        Assert.NotNull(snap);
        Assert.True(snap.HasBattery);
        Assert.Equal(58.59, snap.RemainingWh, 3);
        Assert.Equal(74.16, snap.FullWh, 3);
        Assert.Equal(79, snap.Percent);          // 58590 / 74160 = 79.0%
        Assert.False(snap.AcOnline);
        Assert.False(snap.Charging);
    }

    [Fact]
    public void 交流电设备在线时判为接电()
    {
        using var fs = new SysfsFixture()
            .Battery(status: "Charging", energyNow: 30_000_000, energyFull: 60_000_000)
            .Mains(online: 1);

        var snap = fs.Reader.GetSnapshot();

        Assert.NotNull(snap);
        Assert.True(snap.AcOnline);
        Assert.True(snap.Charging);
        Assert.Equal(50, snap.Percent);
    }

    [Fact]
    public void 交流电设备离线时判为未接电()
    {
        using var fs = new SysfsFixture()
            .Battery(status: "Discharging", energyNow: 30_000_000, energyFull: 60_000_000)
            .Mains(online: 0);

        var snap = fs.Reader.GetSnapshot();

        Assert.NotNull(snap);
        Assert.False(snap.AcOnline);
    }

    /// <summary>已充满时内核把 status 报成 Full，这时仍然接着电（对应 Windows 侧 status=3 的语义）。</summary>
    [Theory]
    [InlineData("Full")]
    [InlineData("Not charging")]
    public void 没有交流电设备时已充满与未充电也算接电(string status)
    {
        using var fs = new SysfsFixture().Battery(
            status: status, energyNow: 60_000_000, energyFull: 60_000_000);

        var snap = fs.Reader.GetSnapshot();

        Assert.NotNull(snap);
        Assert.True(snap.AcOnline);
        Assert.False(snap.Charging);   // Full / Not charging 都不是"正在充"
    }

    /// <summary>没有 energy_* 的驱动要退回 charge_*（µAh）× voltage_now（µV）。</summary>
    [Fact]
    public void 没有能量文件时用电荷乘电压算容量()
    {
        using var fs = new SysfsFixture().Battery(
            status: "Discharging",
            chargeNow: 4_000_000, chargeFull: 5_000_000, voltageNow: 12_000_000);

        var snap = fs.Reader.GetSnapshot();

        Assert.NotNull(snap);
        Assert.Equal(48.0, snap.RemainingWh, 3);   // 4Ah × 12V
        Assert.Equal(60.0, snap.FullWh, 3);        // 5Ah × 12V
        Assert.Equal(80, snap.Percent);
    }

    /// <summary>少数驱动只给 capacity/status。这时容量未知，但电量百分比仍然有效，
    /// HasBattery 必须为 true，否则低电量提醒会静默失效。</summary>
    [Fact]
    public void 只有容量百分比时仍算有电池()
    {
        using var fs = new SysfsFixture().Battery(status: "Discharging", capacity: 15);

        var snap = fs.Reader.GetSnapshot();

        Assert.NotNull(snap);
        Assert.True(snap.HasBattery);
        Assert.Equal(0, snap.FullWh);
        Assert.Equal(15, snap.Percent);
    }

    [Fact]
    public void 多电池按容量累加()
    {
        using var fs = new SysfsFixture()
            .Battery("BAT0", status: "Discharging", energyNow: 10_000_000, energyFull: 40_000_000)
            .Battery("BAT1", status: "Discharging", energyNow: 5_000_000, energyFull: 10_000_000);

        var snap = fs.Reader.GetSnapshot();

        Assert.NotNull(snap);
        Assert.Equal(15.0, snap.RemainingWh, 3);
        Assert.Equal(50.0, snap.FullWh, 3);
        Assert.Equal(30, snap.Percent);   // 15/50
    }

    [Fact]
    public void 非电池设备被跳过()
    {
        using var fs = new SysfsFixture()
            .Other("UPS0", "UPS")
            .Other("USB0", "USB")
            .Battery(status: "Discharging", energyNow: 10_000_000, energyFull: 20_000_000);

        var snap = fs.Reader.GetSnapshot();

        Assert.NotNull(snap);
        Assert.Equal(20.0, snap.FullWh, 3);   // 只算 BAT0，不含 UPS/USB
    }

    [Fact]
    public void 没有电池设备返回null()
    {
        using var fs = new SysfsFixture().Mains(online: 1);

        Assert.Null(fs.Reader.GetSnapshot());
    }

    [Fact]
    public void 根路径不存在返回null()
    {
        var reader = new LinuxBatteryReader(
            Path.Combine(Path.GetTempPath(), "endfieldcharge-does-not-exist-" + Guid.NewGuid().ToString("N")));

        Assert.Null(reader.GetSnapshot());
    }

    /// <summary>设备可能在读取过程中被拔掉，属性文件缺失或内容非法时不能抛。</summary>
    [Fact]
    public void 属性文件内容非法时不抛异常()
    {
        using var fs = new SysfsFixture();
        var dir = Path.Combine(Path.GetTempPath(), "endfieldcharge-sysfs-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "BAT0"));
        File.WriteAllText(Path.Combine(dir, "BAT0", "type"), "Battery\n");
        File.WriteAllText(Path.Combine(dir, "BAT0", "capacity"), "not-a-number\n");
        File.WriteAllText(Path.Combine(dir, "BAT0", "energy_now"), "\n");

        try
        {
            var snap = new LinuxBatteryReader(dir).GetSnapshot();

            Assert.NotNull(snap);
            Assert.True(snap.HasBattery);
            Assert.Equal(0, snap.Percent);   // 读不出来就是 0，而不是崩
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
