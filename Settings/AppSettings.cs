namespace EndfieldCharge.Settings;

/// <summary>
/// 应用设置。
///
/// 刻意写成「主构造函数 + 可选参数默认值」而不是「无参构造 + 属性初始化器」：
/// JSON 源生成对 init 属性只能走「构造参数数组 + 对象初始化器」这条合成路径，
/// 而那条路径会把 JSON 里缺失的字段显式赋成 CLR 默认值（0 / false / null），
/// 覆盖掉属性上写的默认值。旧版本写下的 settings.json 天然缺新加的字段，
/// 那样就等于把用户的设置静默重置。走真实构造函数后，缺字段会回落到这里的参数默认值。
/// （见 tests/EndfieldCharge.Tests/SettingsJsonTests.cs 的「缺字段回落到声明的默认值」）
/// </summary>
/// <param name="GlobalScale">HUD 全局缩放。</param>
/// <param name="DisplayDurationSeconds">HUD 总时长（秒）。前段入场动画保持固定节奏，停留段随该值伸缩。</param>
/// <param name="BounceStrength">回弹强度 0~0.5。映射到 KS_BackOut 第二控制点 Y = 1 + 值，越大过冲越明显。</param>
/// <param name="RippleIntensity">波纹强度倍率 0~2。乘到各圈波纹峰值透明度上。</param>
/// <param name="RippleSpread">波纹幅度倍率 0.5~1.5。乘到各圈波纹最终扩散 scale 上。</param>
/// <param name="HudPosition">HUD 贴在屏幕的哪个位置。</param>
/// <param name="MonitorIndex">HUD 所在显示器：-1 => 主显示器（默认）。</param>
/// <param name="Language">界面语言："auto" / "zh" / "en"。</param>
/// <param name="LowBatteryThreshold">低电量提醒阈值（%）。</param>
/// <param name="EnableLowBatteryAlert">是否启用低电量提醒。</param>
/// <param name="EnableFullChargeAlert">是否在充满时提醒。</param>
/// <param name="EnablePowerSaverNotify">省电模式切换时是否显示 HUD。</param>
/// <param name="EnableAutoStart">是否开机自启。</param>
public sealed record AppSettings(
    double GlobalScale = 0.8,
    double DisplayDurationSeconds = 6.0,
    double BounceStrength = 0.275,
    double RippleIntensity = 1.0,
    double RippleSpread = 1.0,
    HudPosition HudPosition = HudPosition.TopCenter,
    int MonitorIndex = -1,
    string Language = "auto",
    int LowBatteryThreshold = 20,
    bool EnableLowBatteryAlert = true,
    bool EnableFullChargeAlert = true,
    bool EnablePowerSaverNotify = true,
    bool EnableAutoStart = false);

/// <summary>
/// HUD 位置。
///
/// 注意：settings.json 里存的是这个枚举的**数字**取值（声明顺序即取值）。
/// 往中间插入成员会改变已有设置的含义，只能往末尾追加。
/// </summary>
public enum HudPosition
{
    TopCenter,
    TopRight,
    TopLeft,
}
