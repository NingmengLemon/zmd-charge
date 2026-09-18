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
        alert.RenderTransform = new ScaleTransform(0.92, 0.92);
        alert.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

        alert.Show();

        await FadeAsync(alert, 0d, 1d, FadeInDuration, new QuadraticEaseOut());
        await Task.Delay(HoldDuration);
        await FadeAsync(alert, 1d, 0d, FadeOutDuration, new QuadraticEaseIn());

        if (alert.IsVisible)
            alert.Close();
    }

    private static Task FadeAsync(Animatable target, double from, double to, TimeSpan duration, Easing easing)
    {
        var animation = new Animation
        {
            Duration = duration,
            FillMode = FillMode.Forward,
            Easing = easing,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(Visual.OpacityProperty, from) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(Visual.OpacityProperty, to) } },
            },
        };

        return animation.RunAsync(target);
    }
}
