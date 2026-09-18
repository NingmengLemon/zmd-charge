using EndfieldCharge.Services;
using Xunit;

namespace EndfieldCharge.Tests;

public class BatteryServiceTests
{
    [Theory]
    [InlineData(50.0, 100.0, 50)]
    [InlineData(0.0, 100.0, 0)]
    [InlineData(100.0, 100.0, 100)]
    [InlineData(62.4, 90.0, 69)]
    // 本机实测值：满充 74160mWh / 剩余 58590mWh，系统报 79%
    [InlineData(58.590, 74.160, 79)]
    public void ComputePercent_按容量比取整(double remainingWh, double fullWh, int expected)
    {
        Assert.Equal(expected, BatteryService.ComputePercent(remainingWh, fullWh));
    }

    [Theory]
    [InlineData(150.0, 100.0, 100)]  // 超过满充（部分固件会这样报）→ 夹到 100
    [InlineData(-5.0, 100.0, 0)]     // 负数 → 夹到 0
    [InlineData(50.0, 0.0, 0)]       // 容量为 0 不能除零
    [InlineData(50.0, -1.0, 0)]
    public void ComputePercent_越界与非法输入被夹住(double remainingWh, double fullWh, int expected)
    {
        Assert.Equal(expected, BatteryService.ComputePercent(remainingWh, fullWh));
    }

    [Fact]
    public void ComputePercent_结果始终在0到100之间()
    {
        for (double full = 1; full <= 200; full += 7)
        {
            for (double remaining = -50; remaining <= 250; remaining += 11)
            {
                int pct = BatteryService.ComputePercent(remaining, full);
                Assert.InRange(pct, 0, 100);
            }
        }
    }

    [Fact]
    public void HasBattery_以满充容量是否为正判断()
    {
        var withBattery = new BatterySnapshot(50, 90, 55, AcOnline: false, Charging: false);
        var without = new BatterySnapshot(0, 0, 0, AcOnline: true, Charging: false);

        Assert.True(withBattery.HasBattery);
        Assert.False(without.HasBattery);
    }

    /// <summary>
    /// Win32_Battery.BatteryStatus 的映射。旧实现把 {2,6,7,8,9} 同时当成
    /// AcOnline 和 Charging，于是"接了 AC 但没在充电"（status=2/3/11）
    /// 会被误报为充电中，而提醒逻辑依赖这个区别。
    /// </summary>
    [Theory]
    [InlineData(1, false, false)]   // 放电
    [InlineData(2, true, false)]    // 接 AC，不一定在充
    [InlineData(3, true, false)]    // 已充满
    [InlineData(4, false, false)]   // 低
    [InlineData(5, false, false)]   // 危急
    [InlineData(6, true, true)]     // 充电中
    [InlineData(7, true, true)]
    [InlineData(8, true, true)]
    [InlineData(9, true, true)]
    [InlineData(10, false, false)]  // 未定义
    [InlineData(11, true, false)]   // 部分充电（接电，未在充）
    public void WmiBatteryStatus_AC与充电语义分开(ushort status, bool acOnline, bool charging)
    {
        Assert.Equal(acOnline, BatteryService.IsAcOnline(status));
        Assert.Equal(charging, BatteryService.IsCharging(status));
    }

    [Fact]
    public void WmiBatteryStatus_充电一定是接电的()
    {
        for (ushort status = 0; status <= 12; status++)
        {
            if (BatteryService.IsCharging(status))
                Assert.True(BatteryService.IsAcOnline(status), $"status={status} 判为充电却不认为接电");
        }
    }
}
