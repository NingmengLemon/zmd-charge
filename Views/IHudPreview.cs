using EndfieldCharge.Animations;
using EndfieldCharge.Services;

namespace EndfieldCharge.Views;

/// <summary>
/// 设置窗「预览动画」所需的最小 HUD 能力。
///
/// 抽成接口是为了让 SettingsViewModel 不直接依赖 Window：ViewModel 只表达
/// 「让 HUD 播一段」，由谁实现、怎么实现与它无关。单测里可以换成假的实现，
/// 不必真的起一个窗口。
/// </summary>
public interface IHudPreview
{
    Task ShowAndPlayAsync(
        BatterySnapshot? battery,
        HudPlayMode mode = HudPlayMode.Charge,
        AnimationOptions? options = null);

    Task ShowSimpleAsync(BatterySnapshot? battery, AnimationOptions? options = null);
}
