using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace EndfieldCharge.Views;

/// <summary>
/// 卡牌风格提醒窗（低电量 / 已充满 / 检查更新结果），弹出 4 秒后自动消失。
///
/// 原来这个窗口是在 App.axaml.cs 里用全限定名一行行搭出来的（Avalonia.Media.SolidColorBrush
/// 之类），配色也硬编码在代码里。搬进 XAML 后配色统一走 Styles/HudTheme.axaml。
/// </summary>
public partial class AlertWindow : Window
{
    private static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan HoldDuration = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromMilliseconds(120);

    /// <summary>入场时的起始缩放。要跟 <see cref="ShowAsync"/> 里那条缩放轨道保持一致。</summary>
    private const double EnterScale = 0.92d;

    public AlertWindow()
    {
        InitializeComponent();
    }

    private AlertWindow(string title, string message) : this()
    {
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
    }

    /// <summary>弹一个提醒，等它自己淡出关闭。</summary>
    public static async Task ShowAsync(string title, string message)
    {
        var alert = new AlertWindow(title, message);

        // 入场起点：透明 + 略小，随后淡入放大
        alert.Opacity = 0;
        alert.RenderTransform = new ScaleTransform(EnterScale, EnterScale);
        alert.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

        alert.Show();

        // 缩放必须和透明度一起动：只把 RenderTransform 设成 0.92 而不推回 1.0 的话，
        // 提醒窗会永远以 92% 显示（实测窗口 547x225 物理，而声明的是 340x140 逻辑 × 1.75）。
        await AnimateAsync(alert, FadeInDuration, new QuadraticEaseOut(),
            (Visual.OpacityProperty, 0d, 1d),
            (ScaleTransform.ScaleXProperty, EnterScale, 1d),
            (ScaleTransform.ScaleYProperty, EnterScale, 1d));

        await Task.Delay(HoldDuration);

        await AnimateAsync(alert, FadeOutDuration, new QuadraticEaseIn(),
            (Visual.OpacityProperty, 1d, 0d));

        if (alert.IsVisible)
            alert.Close();
    }

    /// <summary>跑一条多轨道动画：每条轨道给 (属性, 起始值, 结束值)。</summary>
    private static Task AnimateAsync(
        Animatable target,
        TimeSpan duration,
        Easing easing,
        params (AvaloniaProperty Property, double From, double To)[] tracks)
    {
        var start = new KeyFrame { Cue = new Cue(0d) };
        var end = new KeyFrame { Cue = new Cue(1d) };

        foreach (var (property, from, to) in tracks)
        {
            start.Setters.Add(new Setter(property, from));
            end.Setters.Add(new Setter(property, to));
        }

        var animation = new Animation
        {
            Duration = duration,
            FillMode = FillMode.Forward,
            Easing = easing,
        };
        animation.Children.Add(start);
        animation.Children.Add(end);

        return animation.RunAsync(target);
    }
}
