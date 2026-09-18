using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using EndfieldCharge.Services;
using EndfieldCharge.Settings;
using EndfieldCharge.Views;

namespace EndfieldCharge;

public partial class App : Application
{
    private PowerWatcher? _watcher;
    private HudWindow? _hud;
    private TrayIcon? _tray;
    private TrayMenuWindow? _trayMenu;
    private SettingsWindow? _settingsWindow;
    private AppSettings _settings = new();
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private DispatcherTimer? _alertTimer;
    private EventWaitHandle? _showHudEvent;
    private Thread? _showHudThread;
    private volatile bool _exiting;
    private bool _lastLowBatteryNotified;
    private bool _lastFullChargeNotified;

    /// <summary>提醒的独立采样间隔。提醒不能挂在 HUD 触发上：放电过程中没有 HUD 事件，
    /// 那样两个提醒基本永远不会弹。</summary>
    private static readonly TimeSpan AlertPollInterval = TimeSpan.FromSeconds(30);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        _desktop = desktop;

        // 加载设置
        _settings = SettingsManager.Load();
        Localization.UseSettings(_settings);
        Logger.Enabled = true; // 可改为设置项

        // 全局未捕获异常兜底
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Logger.Error(e.ExceptionObject as Exception ?? new Exception("Unknown unhandled error"));
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Logger.Error(e.Exception);
            e.SetObserved();
        };

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        desktop.Exit += OnDesktopExit;

        _hud = new HudWindow();
        _hud.ApplySettings(_settings);

        SetupTrayIcon();
        StartPowerWatching();
        StartAlertWatching();
        StartShowHudListener();

        // 调试命令行参数
        if (HasCommandLineArg("--demo"))
            _ = PreviewWithSampleDataAsync();
        else if (HasCommandLineArg("--preview-unplug"))
            _ = PreviewSimpleAsync();
        else if (HasCommandLineArg("--preview"))
            _ = TriggerHudAsync();

        base.OnFrameworkInitializationCompleted();
    }

    // ---------------- 设置 ----------------

    public void OnSettingsChanged(AppSettings settings)
    {
        _settings = settings;
        Localization.UseSettings(settings);
        _hud?.ApplySettings(settings);

        // 更新托盘提示
        if (_tray is not null)
            _tray.ToolTipText = Localization.TrayTooltip;
    }

    // ---------------- 命令行参数 ----------------

    private static bool HasCommandLineArg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (int i = 1; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // ---------------- 预览 ----------------

    private async Task PreviewWithSampleDataAsync()
    {
        if (_hud is null) return;

        var sample = new BatterySnapshot(
            RemainingWh: 62.4, FullWh: 90.0,
            Percent: 69, AcOnline: true, Charging: true);

        await _hud.ShowAndPlayAsync(sample);
    }

    private async Task PreviewSimpleAsync()
    {
        if (_hud is null) return;

        var sample = new BatterySnapshot(
            RemainingWh: 62.4, FullWh: 90.0,
            Percent: 69, AcOnline: false, Charging: false);

        await _hud.ShowSimpleAsync(sample);
    }

    // ---------------- 电源监听 ----------------

    private void StartPowerWatching()
    {
        _watcher = new PowerWatcher();

        _watcher.PowerSourceChanged += (_, acOnline) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (acOnline)
                    _ = TriggerHudAsync();
                else
                    _ = TriggerSimpleHudAsync();

                // 插拔瞬间立刻复核一次提醒条件，不必等下一个轮询 tick
                _ = CheckAlertsAsync();
            });
        };

        // 省电模式开关：开启 → 完整三态（省电文案）；关闭 → 简化电量胶囊
        _watcher.PowerSavingChanged += (_, enabled) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_settings.EnablePowerSaverNotify)
                    return;

                if (enabled)
                    _ = TriggerSaverHudAsync();
                else
                    _ = TriggerSimpleHudAsync();
            });
        };

        _watcher.Start();
    }

    private async Task TriggerSaverHudAsync()
    {
        if (_hud is null) return;

        var snapshot = await Task.Run(BatteryService.GetSnapshot);
        await _hud.ShowAndPlayAsync(snapshot, HudPlayMode.PowerSaver);
    }

    private async Task TriggerSimpleHudAsync()
    {
        if (_hud is null) return;

        var snapshot = await Task.Run(BatteryService.GetSnapshot);
        await _hud.ShowSimpleAsync(snapshot);
    }

    private async Task TriggerHudAsync()
    {
        if (_hud is null) return;

        var snapshot = await Task.Run(BatteryService.GetSnapshot);
        await _hud.ShowAndPlayAsync(snapshot);
    }

    // ---------------- 提醒（独立于 HUD 触发） ----------------

    private void StartAlertWatching()
    {
        _alertTimer = new DispatcherTimer { Interval = AlertPollInterval };
        _alertTimer.Tick += async (_, _) => await CheckAlertsAsync();
        _alertTimer.Start();

        // 启动时先复核一次：开机即处于低电量、或插着电已充满的情况
        _ = CheckAlertsAsync();
    }

    private async Task CheckAlertsAsync()
    {
        if (!_settings.EnableLowBatteryAlert && !_settings.EnableFullChargeAlert)
            return;

        BatterySnapshot? snap;
        try
        {
            snap = await Task.Run(BatteryService.GetSnapshot);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            return;
        }

        if (snap is null || !snap.HasBattery)
            return;

        CheckAlerts(snap);
    }

    /// <summary>检查并触发低电量 / 充满提醒。</summary>
    private void CheckAlerts(BatterySnapshot snap)
    {
        var kind = AlertPolicy.Decide(
            snap,
            _settings.LowBatteryThreshold,
            _settings.EnableLowBatteryAlert,
            _settings.EnableFullChargeAlert,
            _lastLowBatteryNotified,
            _lastFullChargeNotified);

        // 提醒位跟着"条件是否成立"走：条件成立就置位（同一次放电/充满不重复弹），
        // 条件消失就复位（下一轮再满足时可以再弹一次）。
        _lastFullChargeNotified =
            snap.AcOnline && snap.Percent >= AlertPolicy.FullChargePercent;
        _lastLowBatteryNotified =
            !snap.AcOnline && snap.Percent <= _settings.LowBatteryThreshold;

        switch (kind)
        {
            case AlertKind.FullCharge:
                _ = ShowAlertAsync(Localization.FullChargeTitle, Localization.FullChargeMsg);
                break;
            case AlertKind.LowBattery:
                _ = ShowAlertAsync(Localization.LowBatteryTitle, Localization.LowBatteryMsg(snap.Percent));
                break;
        }
    }

    /// <summary>弹出一个卡牌风格提醒窗口，4 秒后自动消失。</summary>
    private static async Task ShowAlertAsync(string title, string message)
    {
        var alert = new Window
        {
            Title = title,
            Width = 340, Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#18181A")),
            Foreground = Avalonia.Media.Brushes.White,
            CanResize = false,
            SystemDecorations = SystemDecorations.BorderOnly,
            Topmost = true,
            FontFamily = new Avalonia.Media.FontFamily("HarmonyOS Sans SC, HarmonyOS Sans, Inter, Microsoft YaHei UI, sans-serif"),
        };

        var card = new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#232325")),
            CornerRadius = new Avalonia.CornerRadius(10),
            Margin = new Avalonia.Thickness(12),
            Padding = new Avalonia.Thickness(18, 16),
        };

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#C6CA4C")),
        });
        stack.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 13,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#C8C8C8")),
        });
        card.Child = stack;
        alert.Content = card;

        // 入场动画
        alert.Opacity = 0;
        alert.RenderTransform = new Avalonia.Media.ScaleTransform(0.92, 0.92);
        alert.RenderTransformOrigin = new Avalonia.RelativePoint(0.5, 0.5, Avalonia.RelativeUnit.Relative);

        alert.Show();

        var fadeIn = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromMilliseconds(150),
            FillMode = Avalonia.Animation.FillMode.Forward,
            Easing = new Avalonia.Animation.Easings.QuadraticEaseOut(),
            Children =
            {
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0d), Setters = { new Avalonia.Styling.Setter(Avalonia.Visual.OpacityProperty, 0d) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(1d), Setters = { new Avalonia.Styling.Setter(Avalonia.Visual.OpacityProperty, 1d) } },
            },
        };
        _ = fadeIn.RunAsync(alert);

        await Task.Delay(4000);

        // 退场
        var fadeOut = new Avalonia.Animation.Animation
        {
            Duration = TimeSpan.FromMilliseconds(120),
            FillMode = Avalonia.Animation.FillMode.Forward,
            Easing = new Avalonia.Animation.Easings.QuadraticEaseIn(),
            Children =
            {
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(0d), Setters = { new Avalonia.Styling.Setter(Avalonia.Visual.OpacityProperty, 1d) } },
                new Avalonia.Animation.KeyFrame { Cue = new Avalonia.Animation.Cue(1d), Setters = { new Avalonia.Styling.Setter(Avalonia.Visual.OpacityProperty, 0d) } },
            },
        };
        await fadeOut.RunAsync(alert);
        if (alert.IsVisible)
            alert.Close();
    }

    // ---------------- 第二个实例的唤醒通路 ----------------

    private void StartShowHudListener()
    {
        try
        {
            _showHudEvent = new EventWaitHandle(
                initialState: false, EventResetMode.AutoReset, Program.ShowHudEventName);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            return;
        }

        _showHudThread = new Thread(() =>
        {
            while (true)
            {
                _showHudEvent!.WaitOne();
                if (_exiting)
                    return;

                Dispatcher.UIThread.Post(() => _ = TriggerHudAsync());
            }
        })
        {
            IsBackground = true,
            Name = "ShowHudListener",
        };
        _showHudThread.Start();
    }

    // ---------------- 托盘 ----------------

    private void SetupTrayIcon()
    {
        // 自定义菜单（TrayMenuWindow）：左键托盘弹出。
        // 不设原生 Menu——11.2 中右键仅在 Menu 非空时弹原生菜单，置空后右键无动作。
        _tray = new TrayIcon
        {
            ToolTipText = Localization.TrayTooltip,
            IsVisible = true,
        };

        _tray.Clicked += OnTrayClicked;

        try
        {
            var uri = new Uri("avares://EndfieldCharge/Assets/tray_bolt.png");
            using var stream = AssetLoader.Open(uri);
            _tray.Icon = new WindowIcon(new Bitmap(stream));
        }
        catch
        {
        }

        var icons = new TrayIcons { _tray };
        TrayIcon.SetIcons(this, icons);
    }

    private void OnTrayClicked(object? sender, EventArgs e)
    {
        // 关闭已打开的菜单
        if (_trayMenu is not null && _trayMenu.IsVisible)
        {
            _trayMenu.Close();
            _trayMenu = null;
            return;
        }

        // 单击托盘图标：立即播放电量预览（真实电池数据，完整三态动画），同时弹出菜单
        _ = TriggerHudAsync();

        _trayMenu = new TrayMenuWindow();
        _trayMenu.PreviewClicked += () => { _trayMenu.Close(); _ = TriggerHudAsync(); };
        _trayMenu.SettingsClicked += () => { _trayMenu.Close(); OpenSettingsWindow(); };
        _trayMenu.CheckUpdateClicked += async () =>
        {
            _trayMenu.Close();
            _trayMenu = null;

            // 对话框需要 owner；_hud 在托盘创建前就已构造，正常不会为空
            if (_hud is not { } owner)
                return;

            try
            {
                var (hasUpdate, version, url) = await UpdateChecker.CheckAsync();
                if (hasUpdate && url is not null)
                {
                    var result = await MessageBox.Show(
                        owner,
                        Localization.UpdateMsg(version ?? "?"),
                        Localization.UpdateTitle,
                        MessageBoxButton.OkCancel);

                    if (result == MessageBoxResult.Ok)
                        Platform.Start(url);
                }
                else
                {
                    await ShowAlertAsync(Localization.CheckUpdate, Localization.UpToDate);
                }
            }
            catch
            {
                await ShowAlertAsync(Localization.CheckUpdate, Localization.UpdateCheckFailed);
            }
        };
        _trayMenu.ExitClicked += () => { _trayMenu.Close(); _desktop?.Shutdown(); };

        // 刷新本地化文字
        _trayMenu.MenuPreviewText.Text = Localization.PreviewHud;
        _trayMenu.MenuSettingsText.Text = Localization.Settings;
        _trayMenu.MenuCheckUpdateText.Text = Localization.CheckUpdate;
        _trayMenu.MenuExitText.Text = Localization.Exit;

        _trayMenu.ShowAtTray();
    }

    private void OpenSettingsWindow(string initialTab = "General")
    {
        // 已开着就把它提到前面，避免连点托盘开出一堆设置窗
        if (_settingsWindow is { } existing)
        {
            existing.Activate();
            return;
        }

        // _hud 在 OnFrameworkInitializationCompleted 中先于托盘创建，此处必非空
        var win = new SettingsWindow(_settings, _hud!, initialTab);
        _settingsWindow = win;
        win.Closed += (_, _) => _settingsWindow = null;
        win.Show();
    }

    // ---------------- 收尾 ----------------

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _exiting = true;

        _alertTimer?.Stop();
        _alertTimer = null;

        // 唤醒等待中的监听线程（它是后台线程，不阻塞退出）
        _showHudEvent?.Set();
        _showHudThread?.Join(TimeSpan.FromSeconds(1));
        _showHudThread = null;
        _showHudEvent?.Dispose();
        _showHudEvent = null;

        _watcher?.Dispose();
        _watcher = null;

        if (_tray is not null)
        {
            _tray.IsVisible = false;
            _tray.Dispose();
            _tray = null;
        }
    }
}