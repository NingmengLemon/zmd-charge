using System;
using System.Linq;
using EndfieldCharge.Animations;
using EndfieldCharge.Settings;
using Xunit;

namespace EndfieldCharge.Tests;

public class HudAnimationsTests
{
    private static AnimationOptions WithDuration(double seconds) =>
        new() { DurationSeconds = seconds };

    [Fact]
    public void 基线时长6秒时映射是恒等()
    {
        var o = WithDuration(6d);

        foreach (double cue in HudAnimations.BaselineCues)
            Assert.Equal(cue, HudAnimations.MapCue(o, cue), 10);
    }

    [Theory]
    [InlineData(3d)]
    [InlineData(4.5d)]
    [InlineData(6d)]
    [InlineData(8d)]
    [InlineData(10d)]
    public void 映射在全部关键帧上严格递增(double duration)
    {
        var o = WithDuration(duration);
        var cues = HudAnimations.BaselineCues;

        // 前置条件：基线 cue 本身必须严格递增，否则这段测试没有意义
        for (int i = 1; i < cues.Length; i++)
            Assert.True(cues[i] > cues[i - 1], $"基线 cue 乱序：{cues[i - 1]} -> {cues[i]}");

        double previous = double.NegativeInfinity;
        foreach (double cue in cues)
        {
            double mapped = HudAnimations.MapCue(o, cue);
            Assert.True(mapped > previous, $"时长 {duration}s 时 {cue} 映射成 {mapped}，未递增");
            previous = mapped;
        }
    }

    [Theory]
    [InlineData(3d)]
    [InlineData(6d)]
    [InlineData(10d)]
    public void 映射两端固定且不越界(double duration)
    {
        var o = WithDuration(duration);

        Assert.Equal(0d, HudAnimations.MapCue(o, 0d), 10);
        Assert.Equal(1d, HudAnimations.MapCue(o, 1d), 10);

        for (double cue = 0; cue <= 1.0; cue += 0.01)
            Assert.InRange(HudAnimations.MapCue(o, cue), 0d, 1d);
    }

    /// <summary>
    /// 入场段（0→0.42）必须保持基线绝对时长 0.42×6=2.52s，
    /// 改时长只拉伸停留段 —— 否则调长时长会把入场动画也拖慢。
    /// </summary>
    [Theory]
    [InlineData(3d)]
    [InlineData(6d)]
    [InlineData(10d)]
    public void 入场段保持绝对时长(double duration)
    {
        var o = WithDuration(duration);

        double introSeconds = HudAnimations.MapCue(o, 0.42d) * duration;

        Assert.Equal(0.42d * 6d, introSeconds, 6);
    }

    [Theory]
    [InlineData(3d)]
    [InlineData(6d)]
    [InlineData(10d)]
    public void 简化版映射同样严格递增且入场固定(double duration)
    {
        var o = WithDuration(duration);
        var cues = HudAnimations.SimpleBaselineCues;

        double previous = double.NegativeInfinity;
        foreach (double cue in cues)
        {
            double mapped = HudAnimations.MapCueSimple(o, cue);
            Assert.True(mapped > previous, $"时长 {duration}s 时 {cue} 映射成 {mapped}，未递增");
            previous = mapped;
        }

        Assert.Equal(0d, HudAnimations.MapCueSimple(o, 0d), 10);
        Assert.Equal(1d, HudAnimations.MapCueSimple(o, 1d), 10);
        Assert.Equal(0.08d * 5d, HudAnimations.MapCueSimple(o, 0.08d) * duration, 6);
    }
}

public class AnimationOptionsTests
{
    [Theory]
    [InlineData(1d, 3d)]
    [InlineData(3d, 3d)]
    [InlineData(6d, 6d)]
    [InlineData(20d, 10d)]
    public void FromSettings_时长夹到3到10秒(double input, double expected)
    {
        var options = AnimationOptions.FromSettings(new AppSettings { DisplayDurationSeconds = input });

        Assert.Equal(expected, options.DurationSeconds, 10);
    }

    [Fact]
    public void FromSettings_其余参数按各自范围夹取()
    {
        var options = AnimationOptions.FromSettings(new AppSettings
        {
            BounceStrength = 9d,
            RippleIntensity = -3d,
            RippleSpread = 100d,
        });

        Assert.Equal(0.5d, options.BounceStrength, 10);
        Assert.Equal(0d, options.RippleIntensity, 10);
        Assert.Equal(1.5d, options.RippleSpread, 10);
    }

    [Fact]
    public void 默认值落在允许范围内()
    {
        var options = AnimationOptions.FromSettings(new AppSettings());

        Assert.InRange(options.DurationSeconds, 3d, 10d);
        Assert.InRange(options.BounceStrength, 0d, 0.5d);
        Assert.InRange(options.RippleIntensity, 0d, 2d);
        Assert.InRange(options.RippleSpread, 0.5d, 1.5d);
        Assert.Equal(Array.Empty<double>(), HudAnimations.BaselineCues.Where(c => c < 0 || c > 1).ToArray());
    }
}
