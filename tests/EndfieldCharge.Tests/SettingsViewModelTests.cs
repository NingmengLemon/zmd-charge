using Avalonia.Controls;
using EndfieldCharge.Animations;
using EndfieldCharge.Services;
using EndfieldCharge.Settings;
using EndfieldCharge.Views;
using Xunit;

namespace EndfieldCharge.Tests;

/// <summary>
/// SettingsViewModel 的纯逻辑单测。
/// 靠 IHudPreview 抽象与 Func&lt;Window&gt; 宿主工厂，这里不需要初始化 Avalonia、
/// 也不会弹任何窗口，更不会碰磁盘上的真实 settings.json。
/// </summary>
public class SettingsViewModelTests
{
    private sealed class FakeHud : IHudPreview
    {
        public List<(BatterySnapshot? Battery, HudPlayMode Mode, AnimationOptions? Options)> Played { get; } = [];
        public List<(BatterySnapshot? Battery, AnimationOptions? Options)> Simple { get; } = [];

        public Task ShowAndPlayAsync(
            BatterySnapshot? battery,
            HudPlayMode mode = HudPlayMode.Charge,
            AnimationOptions? options = null)
        {
            Played.Add((battery, mode, options));
            return Task.CompletedTask;
        }

        public Task ShowSimpleAsync(BatterySnapshot? battery, AnimationOptions? options = null)
        {
            Simple.Add((battery, options));
            return Task.CompletedTask;
        }
    }

    private static readonly MonitorInfo[] TwoMonitors =
    [
        new MonitorInfo(0, true),
        new MonitorInfo(1, false),
    ];

    /// <summary>宿主窗口工厂：这些用例都不该弹对话框，真被调用就直接失败。</summary>
    private static Window NoDialog() =>
        throw new InvalidOperationException("这些用例不应该弹对话框");

    private static SettingsViewModel NewViewModel(
        AppSettings? settings = null,
        IHudPreview? hud = null,
        string initialTab = "General")
        => new(hud ?? new FakeHud(), NoDialog, settings ?? new AppSettings(), TwoMonitors, initialTab);

    [Fact]
    public void 加载再收集应往返一致()
    {
        using var vm = NewViewModel();

        var source = new AppSettings
        {
            GlobalScale = 1.05,
            DisplayDurationSeconds = 8.4,
            BounceStrength = 0.4,
            RippleIntensity = 1.5,
            RippleSpread = 0.7,
            HudPosition = HudPosition.TopLeft,
            MonitorIndex = 1,
            Language = "en",
            LowBatteryThreshold = 35,
            EnableLowBatteryAlert = false,
            EnableFullChargeAlert = false,
            EnablePowerSaverNotify = false,
            EnableAutoStart = true,
        };

        vm.LoadFrom(source);

        Assert.Equal(source, vm.BuildSettings());
    }

    [Theory]
    [InlineData(1.054, 1.05)]
    [InlineData(1.056, 1.06)]
    public void 全局缩放按两位取整(double input, double expected)
    {
        using var vm = NewViewModel();

        vm.GlobalScale = input;

        Assert.Equal(expected, vm.BuildSettings().GlobalScale);
    }

    [Fact]
    public void 其余数值按各自精度取整()
    {
        using var vm = NewViewModel();

        vm.DisplayDurationSeconds = 8.44;
        vm.BounceStrength = 0.2754;
        vm.RippleIntensity = 1.237;
        vm.RippleSpread = 1.237;

        var built = vm.BuildSettings();

        Assert.Equal(8.4, built.DisplayDurationSeconds);
        Assert.Equal(0.275, built.BounceStrength);
        Assert.Equal(1.24, built.RippleIntensity);
        Assert.Equal(1.24, built.RippleSpread);
    }

    /// <summary>滑块给的是 double，落进设置要取整；用 33.4 / 33.6 避开银行家舍入的中点。</summary>
    [Theory]
    [InlineData(33.4, 33)]
    [InlineData(33.6, 34)]
    public void 低电量阈值从滑块取整(double slider, int expected)
    {
        using var vm = NewViewModel();

        vm.LowBatteryThreshold = slider;

        Assert.Equal(expected, vm.BuildSettings().LowBatteryThreshold);
    }

    /// <summary>下拉第 0 项是「主显示器（默认）」(-1)，其后第 i+1 项对应物理显示器 i。</summary>
    [Fact]
    public void 显示器下拉序号与设置值互相映射()
    {
        using var vm = NewViewModel();

        vm.SelectedMonitorIndex = 2;
        Assert.Equal(1, vm.BuildSettings().MonitorIndex);

        vm.SelectedMonitorIndex = 0;
        Assert.Equal(-1, vm.BuildSettings().MonitorIndex);

        vm.LoadFrom(new AppSettings { MonitorIndex = 1 });
        Assert.Equal(2, vm.SelectedMonitorIndex);
    }

    [Fact]
    public void 越界的显示器序号回退到默认项()
    {
        using var vm = NewViewModel();

        vm.LoadFrom(new AppSettings { MonitorIndex = 99 });

        Assert.Equal(0, vm.SelectedMonitorIndex);
        Assert.Equal(-1, vm.BuildSettings().MonitorIndex);
    }

    [Theory]
    [InlineData("auto", 0)]
    [InlineData("zh", 1)]
    [InlineData("en", 2)]
    [InlineData("fr", 0)]
    public void 语言在下拉序号与代码之间映射(string code, int index)
    {
        using var vm = NewViewModel();

        vm.LoadFrom(new AppSettings { Language = code });
        Assert.Equal(index, vm.SelectedLanguageIndex);

        vm.SelectedLanguageIndex = index;
        var expected = code == "zh" ? "zh" : code == "en" ? "en" : "auto";
        Assert.Equal(expected, vm.BuildSettings().Language);
    }

    [Theory]
    [InlineData("General", SettingsTab.General)]
    [InlineData("Animation", SettingsTab.Animation)]
    [InlineData("Notifications", SettingsTab.Notifications)]
    [InlineData("About", SettingsTab.About)]
    [InlineData("不认识的页签", SettingsTab.General)]
    public void 页签选择与可见性标记一致(string tab, SettingsTab expected)
    {
        using var vm = NewViewModel();

        vm.SelectTabCommand.Execute(tab);

        Assert.Equal(expected, vm.SelectedTab);
        Assert.Equal(expected == SettingsTab.General, vm.IsGeneralTab);
        Assert.Equal(expected == SettingsTab.Animation, vm.IsAnimationTab);
        Assert.Equal(expected == SettingsTab.Notifications, vm.IsNotificationsTab);
        Assert.Equal(expected == SettingsTab.About, vm.IsAboutTab);
    }

    [Fact]
    public void 初始页签按名字生效()
    {
        using var vm = NewViewModel(initialTab: "About");

        Assert.Equal(SettingsTab.About, vm.SelectedTab);
    }

    [Fact]
    public async Task 预览按所选模式调用HUD()
    {
        var hud = new FakeHud();
        using var vm = NewViewModel(hud: hud);

        vm.SelectedPreviewModeIndex = 0;
        await vm.PlayPreviewCommand.ExecuteAsync(null);
        Assert.Equal(HudPlayMode.Charge, Assert.Single(hud.Played).Mode);
        Assert.Empty(hud.Simple);

        vm.SelectedPreviewModeIndex = 1;
        await vm.PlayPreviewCommand.ExecuteAsync(null);
        Assert.Equal(HudPlayMode.PowerSaver, hud.Played[^1].Mode);

        vm.SelectedPreviewModeIndex = 2;
        await vm.PlayPreviewCommand.ExecuteAsync(null);
        Assert.Single(hud.Simple);
    }

    /// <summary>预览用的是滑块当前值，且必须夹到动画参数的合法区间。</summary>
    [Fact]
    public async Task 预览参数被夹到合法区间()
    {
        var hud = new FakeHud();
        using var vm = NewViewModel(hud: hud);

        vm.DisplayDurationSeconds = 99;
        vm.BounceStrength = 9;
        vm.RippleIntensity = 9;
        vm.RippleSpread = 9;

        await vm.PlayPreviewCommand.ExecuteAsync(null);

        var options = Assert.Single(hud.Played).Options;
        Assert.NotNull(options);
        Assert.Equal(10d, options.DurationSeconds);
        Assert.Equal(0.5d, options.BounceStrength);
        Assert.Equal(2d, options.RippleIntensity);
        Assert.Equal(1.5d, options.RippleSpread);
    }

    [Fact]
    public async Task 预览结束后按钮恢复可用()
    {
        using var vm = NewViewModel();

        await vm.PlayPreviewCommand.ExecuteAsync(null);

        Assert.False(vm.IsPlayingPreview);
    }

    /// <summary>语言切换后下拉项文案要跟着换，且不重建集合（否则会丢选中项）。</summary>
    [Fact]
    public void 语言切换后下拉项文案原地更新()
    {
        using var vm = NewViewModel();

        Localization.Current.UseSettings(new AppSettings { Language = "zh" });
        Assert.Equal("顶部居中", vm.Positions[0]);
        Assert.Equal("主显示器（默认）", vm.Monitors[0]);
        Assert.Equal("显示器 1（主）", vm.Monitors[1]);

        vm.SelectedPositionIndex = 2;
        Localization.Current.UseSettings(new AppSettings { Language = "en" });

        Assert.Equal("Top Center", vm.Positions[0]);
        Assert.Equal("Primary Monitor (Default)", vm.Monitors[0]);
        Assert.Equal("Monitor 1 (Primary)", vm.Monitors[1]);
        Assert.Equal("Monitor 2", vm.Monitors[2]);

        // 集合没被换掉，选中项也就还在
        Assert.Equal(2, vm.SelectedPositionIndex);
    }

    [Fact]
    public void 数值读数不随区域设置变化()
    {
        using var vm = NewViewModel();

        vm.GlobalScale = 1.25;
        vm.DisplayDurationSeconds = 6;
        vm.BounceStrength = 0.275;
        vm.RippleIntensity = 1;
        vm.RippleSpread = 1;
        vm.LowBatteryThreshold = 20;

        Assert.Equal("1.25", vm.GlobalScaleText);
        Assert.Equal("6.0s", vm.DisplayDurationText);
        Assert.Equal("0.275", vm.BounceStrengthText);
        Assert.Equal("1.00", vm.RippleIntensityText);
        Assert.Equal("1.00", vm.RippleSpreadText);
        Assert.Equal("20%", vm.LowBatteryThresholdText);
    }

    [Fact]
    public void 释放后不再响应语言切换()
    {
        var vm = NewViewModel();
        Localization.Current.UseSettings(new AppSettings { Language = "zh" });
        Assert.Equal("顶部居中", vm.Positions[0]);

        vm.Dispose();
        Localization.Current.UseSettings(new AppSettings { Language = "en" });

        // 已解订阅：这条 ViewModel 不再跟着变（否则设置窗反复开关会一直堆订阅）
        Assert.Equal("顶部居中", vm.Positions[0]);
    }
}
