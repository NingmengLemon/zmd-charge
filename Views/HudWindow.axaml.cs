using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.Styling;
using Avalonia.Threading;
using EndfieldCharge.Animations;
using EndfieldCharge.Services;
using EndfieldCharge.Settings;

namespace EndfieldCharge.Views;

/// <summary>完整三态动画的文案主题：充电（超充模式）或省电模式。</summary>
public enum HudPlayMode
{
    Charge,
    PowerSaver,
}

/// <summary>
/// CA1001：本类持有可释放的 _cts，但释放时机是窗口关闭（OnClosed），不是 IDisposable。
/// Avalonia 的 Window 不实现 IDisposable，自行实现会与未来可能的基类实现打架。
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "释放统一走 Window.OnClosed，这是框架给的生命周期钩子。")]
public partial class HudWindow : Window
{
    private static readonly TimeSpan DismissDuration = TimeSpan.FromMilliseconds(160);

    // 徽章配色同样来自 Styles/HudTheme.axaml，别在这里另抄一份字面量
    private static readonly Color BadgeColorNormal = ResolveThemeColor("Hud.Accent", "#C6CA4C");
    private static readonly Color BadgeColorLow = ResolveThemeColor("Hud.BadgeLow", "#FF4D4F");

    private static Color ResolveThemeColor(string key, string fallback)
        => Application.Current?.TryFindResource(key, out var value) == true && value is Color color
            ? color
            : Color.Parse(fallback);

    /// <summary>胶囊宽度（逻辑像素，与 XAML 一致）。</summary>
    private const double PillWidth = 560d;

    /// <summary>胶囊最大高度：状态 B 的 90。窗口按它留高度，否则状态 B 会被窗口裁掉。</summary>
    private const double PillMaxHeight = 90d;

    private CancellationTokenSource? _cts;
    private AppSettings _settings = new();
    private AnimationOptions _animOptions = AnimationOptions.Default;
    private bool _fpsEnabled;
    private bool _fpsOverlayApplied;

    public HudWindow()
    {
        InitializeComponent();

        TagLineText.Text = Localization.TagLine;
        TitleText.Text = Localization.TitleMode;

        Cursor = new Cursor(StandardCursorType.Hand);
        PointerPressed += (_, _) => _ = DismissAsync();

        _fpsEnabled = Array.Exists(Environment.GetCommandLineArgs(), a => a == "--show-fps");

        ResetToInitial();
    }

    /// <summary>从设置更新 HUD 参数（缩放、动画微调、位置、显示器）。</summary>
    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _animOptions = AnimationOptions.FromSettings(settings);

        // 全局缩放
        GlobalScale.RenderTransform = new ScaleTransform(settings.GlobalScale, settings.GlobalScale);

        // 窗口尺寸跟着缩放走，始终只比可见内容大一圈。
        // 窗口是 Topmost 且背景可命中，开大了就会在插拔那几秒吞掉下方窗口的点击。
        Width = PillWidth * settings.GlobalScale;
        Height = PillMaxHeight * settings.GlobalScale;

        // 更新本地化文本（可能语言变了）
        TagLineText.Text = Localization.TagLine;
        TitleText.Text = Localization.TitleMode;
    }

    // ---------------- 动画播放 ----------------

    public async Task ShowSimpleAsync(BatterySnapshot? battery, AnimationOptions? options = null)
    {
        ApplyBattery(battery);

        var o = options ?? _animOptions;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        ResetToInitial();
        SetSimpleCState();

        ShowPositioned();

        try
        {
            await Task.WhenAll(
                HudAnimations.SimplePillAppear(o).RunAsync(Pill, ct),
                HudAnimations.SimpleFadeIn(o).RunAsync(BoltIcon, ct),
                HudAnimations.SimpleFadeIn(o).RunAsync(NumHost, ct),
                HudAnimations.SimpleScaleOut(o).RunAsync(ScaleHost, ct));
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!ct.IsCancellationRequested && IsVisible)
            Hide();
    }

    public async Task ShowAndPlayAsync(
        BatterySnapshot? battery,
        HudPlayMode mode = HudPlayMode.Charge,
        AnimationOptions? options = null)
    {
        ApplyBattery(battery);

        // 文案主题：充电 = 超充模式；省电 = 省电模式
        TagLineText.Text = mode == HudPlayMode.PowerSaver ? Localization.TagLineSaver : Localization.TagLine;
        TitleText.Text = mode == HudPlayMode.PowerSaver ? Localization.TitleSaver : Localization.TitleMode;

        var o = options ?? _animOptions;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        bool debugStatic = Array.Exists(Environment.GetCommandLineArgs(), a => a == "--debug-ring");

        ResetToInitial();

        ShowPositioned();

        if (debugStatic)
        {
            ShowFullyExpandedStatic();
            try { await Task.Delay(1500, ct); }
            catch (OperationCanceledException) { return; }
            if (!ct.IsCancellationRequested && IsVisible)
                Hide();
            return;
        }

        try
        {
            await Task.WhenAll(
                HudAnimations.PillCorner(o).RunAsync(Pill, ct),
                HudAnimations.PillAppear(o).RunAsync(Pill, ct),
                HudAnimations.PillHeight(o).RunAsync(Pill, ct),
                HudAnimations.PillHeight(o).RunAsync(RippleHost, ct),
                HudAnimations.ScaleOut(o).RunAsync(ScaleHost, ct),
                HudAnimations.BoltIcon(o).RunAsync(BoltIcon, ct),
                HudAnimations.RippleHost(o).RunAsync(RippleHost, ct),
                HudAnimations.CircleForm(o).RunAsync(CircleForm, ct),
                HudAnimations.SquareForm(o).RunAsync(SquareForm, ct),
                HudAnimations.TitleHost(o).RunAsync(TitleHost, ct),
                HudAnimations.NumHost(o).RunAsync(NumHost, ct),
                HudAnimations.Ripple(o, 1.5, 0.50).RunAsync(RippleInner, ct),
                HudAnimations.Ripple(o, 2.0, 0.50).RunAsync(RippleMid, ct),
                HudAnimations.Ripple(o, 2.5, 0.60).RunAsync(RippleOuter, ct),
                HudAnimations.RippleRise(o).RunAsync(RippleInnerHost, ct),
                HudAnimations.RippleRise(o).RunAsync(RippleMidHost, ct),
                HudAnimations.RippleRise(o).RunAsync(RippleOuterHost, ct));
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!ct.IsCancellationRequested && IsVisible)
            Hide();
    }

    private async Task DismissAsync()
    {
        _cts?.Cancel();

        var fade = new Animation
        {
            Duration = DismissDuration,
            FillMode = FillMode.Forward,
            Easing = new QuadraticEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 1d) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 0d) } },
            },
        };

        await fade.RunAsync(Root);
        Hide();
    }

    // ---------------- 数据绑定 ----------------

    private const double RingDiameter = 46d;
    private const double RingThickness = 4.5d;

    private void ApplyBattery(BatterySnapshot? snap)
    {
        double fraction = 0d;

        if (snap is null || !snap.HasBattery)
        {
            WhValueText.Text = "--";
            WhMaxText.Text = string.Empty;
            PercentText.Text = "--";
        }
        else
        {
            // 一律钉死不变文化：电量数字是给用户看的数值，不能随系统区域设置变成
            // 阿拉伯数字/其他小数点符号（CA1305）
            WhValueText.Text = (snap.RemainingWh * 1000).ToString("F0", CultureInfo.InvariantCulture);
            WhMaxText.Text = "/" + (snap.FullWh * 1000).ToString("F0", CultureInfo.InvariantCulture);
            PercentText.Text = snap.Percent.ToString(CultureInfo.InvariantCulture);
            fraction = Math.Clamp(snap.Percent / 100d, 0d, 1d);
        }

        BadgeArc.Data = BuildRingGeometry(fraction, RingDiameter, RingThickness);

        // 变红阈值跟随设置里的低电量阈值，避免和提醒阈值各说各话
        var badgeColor = snap is not null && snap.HasBattery && snap.Percent < _settings.LowBatteryThreshold
            ? BadgeColorLow
            : BadgeColorNormal;
        BadgeArc.Stroke = new SolidColorBrush(badgeColor);
        LaptopScreen.BorderBrush = new SolidColorBrush(badgeColor);
        LaptopBase.Background = new SolidColorBrush(badgeColor);
        BadgeElectrode.Background = new SolidColorBrush(badgeColor);
    }

    private static PathGeometry BuildRingGeometry(double fraction, double diameter, double thickness)
    {
        double radius = (diameter - thickness) / 2d;
        var center = new Point(diameter / 2d, diameter / 2d);

        double sweep = 360d * Math.Clamp(fraction, 0d, 1d);
        if (sweep < 0.5d) sweep = 0.5d;
        if (sweep > 359.5d) sweep = 359.5d;

        const double startAngle = -90d;
        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, startAngle + sweep);

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments = new PathSegments
        {
            new ArcSegment
            {
                Point = end,
                Size = new Size(radius, radius),
                RotationAngle = 0d,
                IsLargeArc = sweep > 180d,
                SweepDirection = SweepDirection.Clockwise,
            },
        };

        return new PathGeometry { Figures = new PathFigures { figure } };
    }

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        double rad = degrees * Math.PI / 180d;
        return new Point(center.X + radius * Math.Cos(rad), center.Y + radius * Math.Sin(rad));
    }

    // ---------------- FPS 调试叠层 ----------------

    /// <summary>
    /// --show-fps 打开 Avalonia 渲染器自带的帧率叠层。
    /// 之前这里自己数 DispatcherTimer 的 tick 数，测出来恒为 ~5"FPS"，
    /// 和真实帧率无关；宁可去掉也不能留一个假指标。
    /// </summary>
    private void ApplyFpsOverlay()
    {
        if (!_fpsEnabled || _fpsOverlayApplied)
            return;

        try
        {
            RendererDiagnostics.DebugOverlays |= RendererDebugOverlays.Fps;
            _fpsOverlayApplied = true;
        }
        catch
        {
            _fpsEnabled = false;
        }
    }

    // ---------------- 动画复位 ----------------

    private void ResetToInitial()
    {
        Root.Opacity = 1;

        ScaleHost.RenderTransform = new ScaleTransform(1d, 1d);

        Pill.Width = 560;
        Pill.Height = 60;
        Pill.CornerRadius = new CornerRadius(30d);
        Pill.Opacity = 0;
        Pill.RenderTransform = new ScaleTransform(0.6d, 0.6d);

        RippleHost.RenderTransform = new TranslateTransform(0d, 0d);

        BoltIcon.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(0.4d, 0.4d), new TranslateTransform(0d, 0d) },
        };
        BoltIcon.Opacity = 0;

        RippleInnerHost.RenderTransform = new TranslateTransform(0d, 16d);
        RippleMidHost.RenderTransform = new TranslateTransform(0d, 16d);
        RippleOuterHost.RenderTransform = new TranslateTransform(0d, 16d);

        RippleInner.RenderTransform = new ScaleTransform(0d, 0d);
        RippleInner.Opacity = 0;
        RippleMid.RenderTransform = new ScaleTransform(0d, 0d);
        RippleMid.Opacity = 0;
        RippleOuter.RenderTransform = new ScaleTransform(0d, 0d);
        RippleOuter.Opacity = 0;

        CircleForm.Opacity = 0;
        SquareForm.Opacity = 0;

        TitleHost.RenderTransform = new TranslateTransform(0d, 0d);
        TitleHost.Opacity = 0;

        NumHost.RenderTransform = new TranslateTransform(0d, 0d);
        NumHost.Opacity = 0;
    }

    private void ShowFullyExpandedStatic()
    {
        ScaleHost.RenderTransform = new ScaleTransform(1d, 1d);
        Pill.Width = 560;
        Pill.Height = 60;
        Pill.CornerRadius = new CornerRadius(30d);
        Pill.Opacity = 1;
        Pill.RenderTransform = new ScaleTransform(1d, 1d);

        RippleHost.RenderTransform = new TranslateTransform(-245d, 0d);

        BoltIcon.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(1d, 1d), new TranslateTransform(-245d, 0d) },
        };
        BoltIcon.Opacity = 1;
        CircleForm.Opacity = 0;
        SquareForm.Opacity = 1;

        TitleHost.Opacity = 0;

        NumHost.RenderTransform = new TranslateTransform(0d, 0d);
        NumHost.Opacity = 1;
    }

    private void SetSimpleCState()
    {
        Pill.Width = 560;
        Pill.Height = 60;
        Pill.CornerRadius = new CornerRadius(30d);
        Pill.Opacity = 0;
        Pill.RenderTransform = new ScaleTransform(0.6d, 0.6d);

        BoltIcon.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(1d, 1d), new TranslateTransform(-245d, 0d) },
        };
        BoltIcon.Opacity = 0;
        CircleForm.Opacity = 0;
        SquareForm.Opacity = 1;

        TitleHost.Opacity = 0;
        RippleHost.Height = 60;
        RippleHost.RenderTransform = new TranslateTransform(0d, 0d);
        RippleInnerHost.RenderTransform = new TranslateTransform(0d, 16d);
        RippleMidHost.RenderTransform = new TranslateTransform(0d, 16d);
        RippleOuterHost.RenderTransform = new TranslateTransform(0d, 16d);
        RippleInner.Opacity = 0;
        RippleMid.Opacity = 0;
        RippleOuter.Opacity = 0;

        NumHost.RenderTransform = new TranslateTransform(0d, 0d);
        NumHost.Opacity = 0;
    }

    // ---------------- 定位（多显示器 + 位置选择） ----------------

    private void PositionTopCenter()
    {
        var screen = ResolveScreen(_settings.MonitorIndex);
        if (screen is null) return;

        var area = screen.WorkingArea;

        // screen.Scaling 来自显示器 DPI 枚举，比窗口的 RenderScaling 可靠（后者首帧前可能未更新）
        double scaling = screen.Scaling > 0 ? screen.Scaling : 1d;
        int pixelWidth = (int)Math.Round(Width * scaling);

        int x = _settings.HudPosition switch
        {
            HudPosition.TopLeft => area.X + 10,
            HudPosition.TopRight => area.X + area.Width - pixelWidth - 10,
            _ => area.X + (area.Width - pixelWidth) / 2, // TopCenter
        };

        Position = new PixelPoint(x, area.Y + 4);
    }

    /// <summary>
    /// 解析目标显示器：-1 = 主显示器（默认），0..N-1 = 显示器列表索引，越界退回主显示器。
    /// </summary>
    private Avalonia.Platform.Screen? ResolveScreen(int monitorIndex)
    {
        var screens = Screens.All;
        var primary = Screens.Primary;

        if (monitorIndex < 0)
            return primary ?? FirstScreen(screens);

        if (monitorIndex < screens.Count)
            return screens[monitorIndex];

        return primary ?? FirstScreen(screens);
    }

    /// <summary>Screens.All 是 IReadOnlyList，直接取下标即可；
    /// 用 FirstOrDefault 会白走一遍枚举器（CA1826）。</summary>
    private static Avalonia.Platform.Screen? FirstScreen(IReadOnlyList<Avalonia.Platform.Screen> screens)
        => screens.Count > 0 ? screens[0] : null;

    private void ShowPositioned()
    {
        PositionTopCenter();

        if (!IsVisible)
            Show();

        ApplyFpsOverlay();
        PositionTopCenter();
        Dispatcher.UIThread.Post(() =>
        {
            PositionTopCenter();
        }, DispatcherPriority.Loaded);
    }

    // ---------------- 收尾 ----------------

    /// <summary>窗口关闭时释放动画取消源。原来 _cts 只被替换、从不释放，是一处真实的泄漏。</summary>
    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        base.OnClosed(e);
    }
}