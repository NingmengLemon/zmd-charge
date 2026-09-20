using System.Diagnostics.CodeAnalysis;
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

/// <summary>
/// CA1001：本类确实持有 IDisposable 字段（IPowerWatcher / TrayIcon / ShowHudChannel），
/// 但它们的释放时机是 ApplicationLifetime 的 Exit（OnDesktopExit），而不是 IDisposable。
/// Avalonia 的 Application 不是 IDisposable，自己实现一个只会多一层没有调用保证的间接。
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "释放统一走 ApplicationLifetime.Exit -> OnDesktopExit，这是框架给的生命周期钩子。")]
public partial class App : Application
{
    private IPowerWatcher? _watcher;
    private HudWindow? _hud;
    private TrayIcon? _tray;
    private SettingsWindow? _settingsWindow;
    private NativeMenuItem? _menuPreview;
    private NativeMenuItem? _menuSettings;
    private NativeMenuItem? _menuCheckUpdate;
    private NativeMenuItem? _menuExit;
    private AppSettings _settings = new();
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private DispatcherTimer? _alertTimer;
    private ShowHudChannel? _showHudChannel;
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
        Localization.Current.UseSettings(_settings);
        Logger.Enabled = true; // 可改为设置项

        // 全局未捕获异常兜底
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            // 异常对象不一定是 Exception，但两种都要把原文记下来，别丢信息
            if (e.ExceptionObject is Exception ex)
                Logger.Error(ex);
            else
                Logger.Error($"Unhandled non-exception object: {e.ExceptionObject}");
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
        Localization.Current.UseSettings(settings);
        _hud?.ApplySettings(settings);
        ApplyTrayLocalization();
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
        _watcher = PowerWatcherFactory.Create();

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
                _ = AlertWindow.ShowAsync(Localization.Current.FullChargeTitle, Localization.Current.FullChargeMsg);
                break;
            case AlertKind.LowBattery:
                _ = AlertWindow.ShowAsync(Localization.Current.LowBatteryTitle, Localization.Current.LowBatteryMsg(snap.Percent));
                break;
        }
    }

    // ---------------- 第二个实例的唤醒通路 ----------------

    private void StartShowHudListener()
    {
        _showHudChannel = ShowHudChannel.Create();
        _showHudChannel.StartListening(() =>
        {
            if (_exiting)
                return;

            Dispatcher.UIThread.Post(() => _ = TriggerHudAsync());
        });
    }

    // ---------------- 托盘 ----------------

    private void SetupTrayIcon()
    {
        _tray = new TrayIcon
        {
            ToolTipText = Localization.Current.TrayTooltip,
            IsVisible = true,
        };

        // 左键：只播放 HUD 动画
        _tray.Clicked += (_, _) => _ = TriggerHudAsync();

        // 右键：Avalonia 只在 Menu 非空时才弹菜单
        // （Win32 TrayIconImpl.OnRightClicked 对空菜单直接 return，置空则右键无动作）
        _tray.Menu = BuildTrayMenu();

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

    /// <summary>右键菜单。菜单项文字在语言切换时由 ApplyTrayLocalization 刷新。</summary>
    private NativeMenu BuildTrayMenu()
    {
        _menuPreview = new NativeMenuItem { Header = Localization.Current.PreviewHud };
        _menuPreview.Click += (_, _) => _ = TriggerHudAsync();

        _menuSettings = new NativeMenuItem { Header = Localization.Current.Settings };
        _menuSettings.Click += (_, _) => OpenSettingsWindow();

        _menuCheckUpdate = new NativeMenuItem { Header = Localization.Current.CheckUpdate };
        _menuCheckUpdate.Click += async (_, _) => await CheckUpdateFromTrayAsync();

        _menuExit = new NativeMenuItem { Header = Localization.Current.Exit };
        _menuExit.Click += (_, _) => _desktop?.Shutdown();

        var menu = new NativeMenu();
        menu.Add(_menuPreview);
        menu.Add(_menuSettings);
        menu.Add(_menuCheckUpdate);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(_menuExit);
        return menu;
    }

    private void ApplyTrayLocalization()
    {
        if (_tray is not null)
            _tray.ToolTipText = Localization.Current.TrayTooltip;

        if (_menuPreview is not null) _menuPreview.Header = Localization.Current.PreviewHud;
        if (_menuSettings is not null) _menuSettings.Header = Localization.Current.Settings;
        if (_menuCheckUpdate is not null) _menuCheckUpdate.Header = Localization.Current.CheckUpdate;
        if (_menuExit is not null) _menuExit.Header = Localization.Current.Exit;
    }

    private async Task CheckUpdateFromTrayAsync()
    {
        // 对话框需要 owner；_hud 在托盘创建前就已构造，正常不会为空
        if (_hud is not { } owner)
            return;

        try
        {
            var (hasUpdate, version, url) = await UpdateChecker.CheckAsync();
            if (hasUpdate && url is not null)
            {
                var result = await MessageBoxWindow.ShowAsync(
                    owner,
                    Localization.Current.UpdateMsg(version ?? "?"),
                    Localization.Current.UpdateTitle,
                    MessageBoxButton.OkCancel);

                if (result == MessageBoxResult.Ok)
                    UrlLauncher.Open(url);
            }
            else
            {
                await AlertWindow.ShowAsync(Localization.Current.CheckUpdate, Localization.Current.UpToDate);
            }
        }
        catch
        {
            await AlertWindow.ShowAsync(Localization.Current.CheckUpdate, Localization.Current.UpdateCheckFailed);
        }
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

        // 关掉唤醒通路（内部会唤醒并回收监听线程）
        _showHudChannel?.Dispose();
        _showHudChannel = null;

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