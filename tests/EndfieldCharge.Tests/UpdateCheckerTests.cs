using System;
using EndfieldCharge.Services;
using Xunit;

namespace EndfieldCharge.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v1.2", 1, 2, 0)]
    [InlineData("v1.2.3-dev", 1, 2, 3)]
    [InlineData("v1.2.3-beta.1", 1, 2, 3)]
    [InlineData("v10.20.30", 10, 20, 30)]
    public void ParseVersion_解析正常标签(string tag, int major, int minor, int build)
    {
        var v = UpdateChecker.ParseVersion(tag);

        Assert.NotNull(v);
        Assert.Equal(new Version(major, minor, build), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("1")]
    [InlineData("nightly")]
    [InlineData("v1.x")]
    public void ParseVersion_解析不了的返回null(string tag)
    {
        Assert.Null(UpdateChecker.ParseVersion(tag));
    }

    /// <summary>
    /// 版本比较的边界：带 build 的 latest 必须大于同号的 4 段程序集版本，
    /// 否则"当前版本 == 最新 tag"会被误判成有更新。
    /// </summary>
    [Fact]
    public void 同版本不算更新_主干构建版本更高()
    {
        var latest = UpdateChecker.ParseVersion("v1.1.1");

        Assert.NotNull(latest);
        // 主干构建注入的第 4 段：1.1.1.500 > 1.1.1
        Assert.False(latest > new Version(1, 1, 1, 500));
        // 发布版自身：1.1.1 == 1.1.1.0 不成立（1.1.1 更小），也不该报更新
        Assert.False(latest > new Version(1, 1, 1, 0));
        // 真的有新版本时才报
        Assert.True(UpdateChecker.ParseVersion("v1.1.2") > new Version(1, 1, 1, 500));
    }
}
