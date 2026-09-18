using EndfieldCharge.Services;
using Xunit;

namespace EndfieldCharge.Tests;

/// <summary>
/// 提醒判定。回归点是"接电且已充满"必须能触发：
/// 旧实现用 snap.Charging 当条件，而 Windows 在充满后会把 Charging 置 0，
/// 加上判定挂在 HUD 触发上，导致充满提醒实际不会弹。
/// </summary>
public class AlertPolicyTests
{
    private static BatterySnapshot Snap(int percent, bool acOnline, bool charging = false) =>
        new(RemainingWh: percent, FullWh: 100, Percent: percent, AcOnline: acOnline, Charging: charging);

    [Fact]
    public void 接电已充满_触发充满提醒()
    {
        // 充满瞬间 Windows 报 Charging=false，这正是旧实现的漏点
        var kind = AlertPolicy.Decide(Snap(100, acOnline: true, charging: false),
            lowThreshold: 20, lowEnabled: true, fullEnabled: true,
            lowAlreadyNotified: false, fullAlreadyNotified: false);

        Assert.Equal(AlertKind.FullCharge, kind);
    }

    [Fact]
    public void 放电且低于阈值_触发低电量提醒()
    {
        var kind = AlertPolicy.Decide(Snap(15, acOnline: false),
            lowThreshold: 20, lowEnabled: true, fullEnabled: true,
            lowAlreadyNotified: false, fullAlreadyNotified: false);

        Assert.Equal(AlertKind.LowBattery, kind);
    }

    [Fact]
    public void 阈值边界_等于阈值也触发()
    {
        var kind = AlertPolicy.Decide(Snap(20, acOnline: false),
            lowThreshold: 20, lowEnabled: true, fullEnabled: true,
            lowAlreadyNotified: false, fullAlreadyNotified: false);

        Assert.Equal(AlertKind.LowBattery, kind);
    }

    [Theory]
    [InlineData(21, false)]   // 高于阈值
    [InlineData(15, true)]    // 接了电，不算低电量
    public void 不满足条件_不触发低电量(int percent, bool acOnline)
    {
        var kind = AlertPolicy.Decide(Snap(percent, acOnline),
            lowThreshold: 20, lowEnabled: true, fullEnabled: true,
            lowAlreadyNotified: false, fullAlreadyNotified: false);

        Assert.NotEqual(AlertKind.LowBattery, kind);
    }

    [Fact]
    public void 已提醒过_同一轮不再弹()
    {
        Assert.Null(AlertPolicy.Decide(Snap(15, acOnline: false),
            lowThreshold: 20, lowEnabled: true, fullEnabled: true,
            lowAlreadyNotified: true, fullAlreadyNotified: false));

        Assert.Null(AlertPolicy.Decide(Snap(100, acOnline: true),
            lowThreshold: 20, lowEnabled: true, fullEnabled: true,
            lowAlreadyNotified: false, fullAlreadyNotified: true));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void 关掉的提醒不会弹(bool lowEnabled, bool fullEnabled)
    {
        var low = AlertPolicy.Decide(Snap(10, acOnline: false),
            lowThreshold: 20, lowEnabled: lowEnabled, fullEnabled: fullEnabled,
            lowAlreadyNotified: false, fullAlreadyNotified: false);
        var full = AlertPolicy.Decide(Snap(100, acOnline: true),
            lowThreshold: 20, lowEnabled: lowEnabled, fullEnabled: fullEnabled,
            lowAlreadyNotified: false, fullAlreadyNotified: false);

        if (!lowEnabled) Assert.NotEqual(AlertKind.LowBattery, low);
        if (!fullEnabled) Assert.NotEqual(AlertKind.FullCharge, full);
    }

    [Fact]
    public void 没有电池_什么都不弹()
    {
        var noBattery = new BatterySnapshot(0, 0, 0, AcOnline: true, Charging: false);

        Assert.Null(AlertPolicy.Decide(noBattery,
            lowThreshold: 20, lowEnabled: true, fullEnabled: true,
            lowAlreadyNotified: false, fullAlreadyNotified: false));
    }

    [Fact]
    public void 接电未充满_不弹任何提醒()
    {
        var kind = AlertPolicy.Decide(Snap(79, acOnline: true, charging: true),
            lowThreshold: 20, lowEnabled: true, fullEnabled: true,
            lowAlreadyNotified: false, fullAlreadyNotified: false);

        Assert.Null(kind);
    }
}
