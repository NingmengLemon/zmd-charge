using System.Text.Json;
using EndfieldCharge.Settings;
using Xunit;

namespace EndfieldCharge.Tests;

/// <summary>
/// settings.json 的磁盘格式是用户数据契约：字段名、枚举取值形式、以及缺字段时的回落
/// 都必须稳定，否则用户已保存的设置会被静默重置。改用 JSON 源生成序列化后，
/// 这几条要重新钉一遍（源生成的构造路径与反射路径在「缺字段」上的行为不保证一致）。
/// </summary>
public class SettingsJsonTests
{
    private static string Serialize(AppSettings s)
        => JsonSerializer.Serialize(s, SettingsJsonContext.Default.AppSettings);

    private static AppSettings? Deserialize(string json)
        => JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);

    [Fact]
    public void 全部字段往返一致()
    {
        var original = new AppSettings
        {
            GlobalScale = 1.25,
            DisplayDurationSeconds = 7.5,
            BounceStrength = 0.4,
            RippleIntensity = 1.7,
            RippleSpread = 0.6,
            HudPosition = HudPosition.TopLeft,
            MonitorIndex = 2,
            Language = "en",
            LowBatteryThreshold = 15,
            EnableLowBatteryAlert = false,
            EnableFullChargeAlert = false,
            EnablePowerSaverNotify = false,
            EnableAutoStart = true,
        };

        Assert.Equal(original, Deserialize(Serialize(original)));
    }

    [Fact]
    public void 字段名是磁盘契约()
    {
        var json = Serialize(new AppSettings());

        foreach (var name in new[]
                 {
                     "GlobalScale", "DisplayDurationSeconds", "BounceStrength", "RippleIntensity",
                     "RippleSpread", "HudPosition", "MonitorIndex", "Language", "LowBatteryThreshold",
                     "EnableLowBatteryAlert", "EnableFullChargeAlert", "EnablePowerSaverNotify",
                     "EnableAutoStart",
                 })
        {
            Assert.Contains($"\"{name}\"", json, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 枚举沿用数字形式（HudPosition 的声明顺序即取值）。
    /// 换成字符串形式会让旧版本写下的数字文件读不出来，属于数据格式变更，不能顺手做。
    /// 反过来说：往 HudPosition 中间插值会改变已有设置的含义，别那样改。
    /// </summary>
    [Fact]
    public void 枚举沿用数字形式()
    {
        var json = Serialize(new AppSettings { HudPosition = HudPosition.TopLeft });

        Assert.Contains("\"HudPosition\": 2", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// 缺字段时必须回落到 AppSettings 里声明的默认值，而不是 CLR 默认值（0 / false / null）。
    /// 旧版本写下的 settings.json 天然缺新加的字段，回落错了就等于把用户设置重置。
    /// </summary>
    [Fact]
    public void 缺字段回落到声明的默认值()
    {
        var settings = Deserialize("{}");

        Assert.NotNull(settings);
        Assert.Equal(new AppSettings(), settings);
    }

    [Fact]
    public void 未知字段被忽略()
    {
        var settings = Deserialize("""{"GlobalScale":0.5,"SomethingFromTheFuture":123}""");

        Assert.NotNull(settings);
        Assert.Equal(0.5, settings.GlobalScale);
        Assert.Equal("auto", settings.Language);
    }

    /// <summary>
    /// 钉住序列化后的确切文本形态（含缩进与字段顺序）。用户磁盘上的 settings.json
    /// 就是长这样，任何格式漂移都算数据格式变更。
    /// 从反射序列化换到源生成序列化时必须确认这条没变。
    /// </summary>
    [Fact]
    public void 序列化文本形态与既有磁盘格式一致()
    {
        const string expectedRaw = """
            {
              "GlobalScale": 0.8,
              "DisplayDurationSeconds": 6,
              "BounceStrength": 0.275,
              "RippleIntensity": 1,
              "RippleSpread": 1,
              "HudPosition": 0,
              "MonitorIndex": -1,
              "Language": "auto",
              "LowBatteryThreshold": 20,
              "EnableLowBatteryAlert": true,
              "EnableFullChargeAlert": true,
              "EnablePowerSaverNotify": true,
              "EnableAutoStart": false
            }
            """;

        // 原始字符串字面量会原样保留源文件的行尾（本仓库工作区是 CRLF），
        // 而这里要钉的是序列化器输出的 LF，所以先归一化，
        // 否则这条用例的成败会随检出行尾而变。
        var expected = expectedRaw.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal(expected, Serialize(new AppSettings()));
    }
}
