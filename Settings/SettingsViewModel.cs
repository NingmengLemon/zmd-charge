using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EndfieldCharge.Animations;
using EndfieldCharge.Services;
using EndfieldCharge.Views;

namespace EndfieldCharge.Settings;

/// <summary>设置窗的四个页签。</summary>
public enum SettingsTab
{
    General,
    Animation,
    Notifications,
    About,
}

/// <summary>
/// 显示器条目。由窗口在构造 ViewModel 时传入，
/// 这样 ViewModel 不必直接接触 Avalonia 的平台类型（Screens / Screen）。
/// </summary>
public readonly record struct MonitorInfo(int Index, bool IsPrimary);

/// <summary>
/// 设置窗的 ViewModel。
///
/// 改造前这些逻辑散在 SettingsWindow.axaml.cs 里：约 55 行手写的本地化赋值、
/// 一份 LoadSettings、一份 CollectSettings、6 组滑块与数值标签的手工同步、
/// 4 个页签的显隐与高亮手工切换。现在值全部走绑定，命令走 [RelayCommand]，
/// 语言切换靠 Localization.NotifyAll() 一次性刷新（空属性名按 INPC 约定表示「全部」）。
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IHudPreview _hud;
    private readonly Func<Window> _owner;
    private readonly MonitorInfo[] _monitors;

    /// <summary>下拉第 0 项是「主显示器（默认）」(-1)，其后第 i+1 项对应物理显示器 i。</summary>
    private readonly int[] _monitorIndices;

    private readonly PropertyChangedEventHandler _localizationChanged;

    /// <summary>预览用的假数据，与改造前一致。</summary>
    private static readonly BatterySnapshot PreviewSample = new(
        RemainingWh: 62.4, FullWh: 90.0, Percent: 69, AcOnline: true, Charging: true);

    /// <param name="hud">预览动画用的 HUD 能力。</param>
    /// <param name="owner">
    /// 弹对话框时的宿主窗口。刻意用工厂而不是直接传 Window：
    /// 只有「检查更新」真正需要它，而单测里构造 Window 会要求 Avalonia 已初始化，
    /// 传工厂就能把这条依赖推迟到真正用的时候。
    /// </param>
    /// <param name="settings">初始设置。</param>
    /// <param name="monitors">显示器列表（Screens 是 TopLevel 的属性，由窗口收集后传入）。</param>
    /// <param name="initialTab">打开时停在哪个页签。</param>
    public SettingsViewModel(
        IHudPreview hud,
        Func<Window> owner,
        AppSettings settings,
        MonitorInfo[] monitors,
        string initialTab = "General")
    {
        _hud = hud;
        _owner = owner;
        _monitors = monitors;

        _monitorIndices = new int[monitors.Length + 1];
        _monitorIndices[0] = -1;
        for (int i = 0; i < monitors.Length; i++)
            _monitorIndices[i + 1] = monitors[i].Index;

        LoadFrom(settings);

        SelectedTab = initialTab switch
        {
            "Animation" => SettingsTab.Animation,
            "Notifications" => SettingsTab.Notifications,
            "About" => SettingsTab.About,
            _ => SettingsTab.General,
        };

        RefreshChoiceNames();

        // 语言一变，下拉项的名字要跟着换。ViewModel 自己管订阅、自己解订阅。
        _localizationChanged = (_, _) => RefreshChoiceNames();
        Localization.Current.PropertyChanged += _localizationChanged;
    }

    /// <summary>文案入口。绑定写成 {Binding L.TabGeneral}，语言切换时自动刷新。</summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "XAML 绑定只能绑实例成员，静态属性无法作为绑定路径的根。")]
    public Localization L => Localization.Current;

    public void Dispose() => Localization.Current.PropertyChanged -= _localizationChanged;

    // ================= 设置值（与 AppSettings 一一对应） =================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GlobalScaleText))]
    private double _globalScale = 0.8;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayDurationText))]
    private double _displayDurationSeconds = 6.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BounceStrengthText))]
    private double _bounceStrength = 0.275;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RippleIntensityText))]
    private double _rippleIntensity = 1.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RippleSpreadText))]
    private double _rippleSpread = 1.0;

    /// <summary>低电量阈值。Slider.Value 是 double，落到设置里时才取整。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LowBatteryThresholdText))]
    private double _lowBatteryThreshold = 20;

    [ObservableProperty]
    private bool _enableLowBatteryAlert = true;

    [ObservableProperty]
    private bool _enableFullChargeAlert = true;

    [ObservableProperty]
    private bool _enablePowerSaverNotify = true;

    [ObservableProperty]
    private bool _enableAutoStart;

    // 数值读数：一律不变文化，不能随系统区域设置换小数点符号（CA1305）
    public string GlobalScaleText => GlobalScale.ToString("F2", CultureInfo.InvariantCulture);
    public string DisplayDurationText => string.Create(CultureInfo.InvariantCulture, $"{DisplayDurationSeconds:F1}s");
    public string BounceStrengthText => BounceStrength.ToString("F3", CultureInfo.InvariantCulture);
    public string RippleIntensityText => RippleIntensity.ToString("F2", CultureInfo.InvariantCulture);
    public string RippleSpreadText => RippleSpread.ToString("F2", CultureInfo.InvariantCulture);
    public string LowBatteryThresholdText => string.Create(CultureInfo.InvariantCulture, $"{LowBatteryThreshold:F0}%");

    // ================= 下拉框 =================

    public ObservableCollection<string> Positions { get; } = [];
    public ObservableCollection<string> Monitors { get; } = [];
    public ObservableCollection<string> Languages { get; } = [];
    public ObservableCollection<string> PreviewModes { get; } = [];

    [ObservableProperty]
    private int _selectedPositionIndex;

    [ObservableProperty]
    private int _selectedMonitorIndex;

    [ObservableProperty]
    private int _selectedLanguageIndex;

    [ObservableProperty]
    private int _selectedPreviewModeIndex;

    /// <summary>
    /// 就地替换下拉项文案。刻意不改集合身份，只按下标赋值，
    /// 这样语言切换不会把用户已经选好的项清掉。
    /// </summary>
    private void RefreshChoiceNames()
    {
        SetNames(Positions, L.PosTopCenter, L.PosTopRight, L.PosTopLeft);
        SetNames(Languages, L.ValueAuto, L.ValueChinese, L.ValueEnglish);
        SetNames(PreviewModes, L.ModePlug, L.ModeSaver, L.ModeUnplug);

        var monitorNames = new string[_monitors.Length + 1];
        monitorNames[0] = L.MonitorPrimaryDefault;
        for (int i = 0; i < _monitors.Length; i++)
            monitorNames[i + 1] = L.MonitorName(_monitors[i].Index, _monitors[i].IsPrimary);

        SetNames(Monitors, monitorNames);
    }

    private static void SetNames(ObservableCollection<string> target, params string[] names)
    {
        while (target.Count < names.Length)
            target.Add(string.Empty);
        while (target.Count > names.Length)
            target.RemoveAt(target.Count - 1);

        for (int i = 0; i < names.Length; i++)
        {
            if (!string.Equals(target[i], names[i], StringComparison.Ordinal))
                target[i] = names[i];
        }
    }

    // ================= 页签 =================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralTab), nameof(IsAnimationTab), nameof(IsNotificationsTab), nameof(IsAboutTab))]
    private SettingsTab _selectedTab;

    public bool IsGeneralTab => SelectedTab == SettingsTab.General;
    public bool IsAnimationTab => SelectedTab == SettingsTab.Animation;
    public bool IsNotificationsTab => SelectedTab == SettingsTab.Notifications;
    public bool IsAboutTab => SelectedTab == SettingsTab.About;

    [RelayCommand]
    private void SelectTab(string tab) => SelectedTab = tab switch
    {
        "Animation" => SettingsTab.Animation,
        "Notifications" => SettingsTab.Notifications,
        "About" => SettingsTab.About,
        _ => SettingsTab.General,
    };

    // ================= 状态文案 =================

    [ObservableProperty]
    private string _updateStatus = string.Empty;

    [ObservableProperty]
    private string _fontStatus = string.Empty;

    [ObservableProperty]
    private bool _isCheckingUpdate;

    [ObservableProperty]
    private bool _isPlayingPreview;

    [ObservableProperty]
    private bool _isInstallingFont;

    /// <summary>「设置已保存」提示的透明度，2 秒后自动归零。</summary>
    [ObservableProperty]
    private double _savedHintOpacity;

    public string VersionText => GetType().Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    // ================= 加载 / 收集 =================

    /// <summary>把设置灌进 ViewModel。</summary>
    public void LoadFrom(AppSettings s)
    {
        GlobalScale = s.GlobalScale;
        DisplayDurationSeconds = s.DisplayDurationSeconds;
        BounceStrength = s.BounceStrength;
        RippleIntensity = s.RippleIntensity;
        RippleSpread = s.RippleSpread;
        LowBatteryThreshold = s.LowBatteryThreshold;

        EnableLowBatteryAlert = s.EnableLowBatteryAlert;
        EnableFullChargeAlert = s.EnableFullChargeAlert;
        EnablePowerSaverNotify = s.EnablePowerSaverNotify;
        EnableAutoStart = s.EnableAutoStart;

        SelectedPositionIndex = (int)s.HudPosition;
        SelectedMonitorIndex = ResolveMonitorComboIndex(s.MonitorIndex);
        SelectedLanguageIndex = s.Language switch
        {
            "zh" => 1,
            "en" => 2,
            _ => 0,
        };
    }

    /// <summary>把 ViewModel 收集成设置。与磁盘写入解耦，便于单测。</summary>
    public AppSettings BuildSettings() => new(
        GlobalScale: Math.Round(GlobalScale, 2),
        DisplayDurationSeconds: Math.Round(DisplayDurationSeconds, 1),
        BounceStrength: Math.Round(BounceStrength, 3),
        RippleIntensity: Math.Round(RippleIntensity, 2),
        RippleSpread: Math.Round(RippleSpread, 2),
        HudPosition: (HudPosition)SelectedPositionIndex,
        MonitorIndex: SelectedMonitorIndex >= 0 && SelectedMonitorIndex < _monitorIndices.Length
            ? _monitorIndices[SelectedMonitorIndex]
            : -1,
        Language: SelectedLanguageIndex switch
        {
            1 => "zh",
            2 => "en",
            _ => "auto",
        },
        LowBatteryThreshold: (int)Math.Round(LowBatteryThreshold),
        EnableLowBatteryAlert: EnableLowBatteryAlert,
        EnableFullChargeAlert: EnableFullChargeAlert,
        EnablePowerSaverNotify: EnablePowerSaverNotify,
        EnableAutoStart: EnableAutoStart);

    private int ResolveMonitorComboIndex(int monitorIndex)
    {
        for (int i = 0; i < _monitorIndices.Length; i++)
        {
            if (_monitorIndices[i] == monitorIndex)
                return i;
        }

        return 0;
    }

    // ================= 命令 =================

    /// <summary>保存成功后抛出，由窗口转交给 App 应用新设置。</summary>
    public event EventHandler<AppSettings>? Saved;

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = BuildSettings();
        SettingsManager.Save(settings);

        if (settings.EnableAutoStart)
            AutoStart.Enable(AutoStart.CurrentExePath);
        else
            AutoStart.Disable();

        // 语言可能刚被改掉：这一步会通知所有绑定刷新，设置窗自己也就跟着变了
        Localization.Current.UseSettings(settings);
        Saved?.Invoke(this, settings);

        SavedHintOpacity = 1;
        await Task.Delay(2000);
        SavedHintOpacity = 0;
    }

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        IsCheckingUpdate = true;
        UpdateStatus = "...";

        try
        {
            var (hasUpdate, version, url) = await UpdateChecker.CheckAsync();
            if (hasUpdate && url is not null)
            {
                var result = await MessageBoxWindow.ShowAsync(
                    _owner(),
                    L.UpdateMsg(version ?? "?"),
                    L.UpdateTitle,
                    MessageBoxButton.OkCancel);

                if (result == MessageBoxResult.Ok)
                    UrlLauncher.Open(url);
            }
            else
            {
                UpdateStatus = L.UpToDate;
            }
        }
        catch
        {
            UpdateStatus = L.UpdateCheckFailed;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task InstallFontAsync()
    {
        IsInstallingFont = true;
        FontStatus = L.FontInstalling;

        // Inter 字体 GitHub Releases 下载页
        const string fontUrl = "https://github.com/rsms/inter/releases/latest";

        try
        {
            UrlLauncher.Open(fontUrl);
            await Task.Delay(500);
            FontStatus = L.FontInstalled;
        }
        catch
        {
            FontStatus = L.UpdateCheckFailed;
        }
        finally
        {
            IsInstallingFont = false;
        }
    }

    /// <summary>用当前滑块值（未保存也生效）实时预览动画。</summary>
    [RelayCommand]
    private async Task PlayPreviewAsync()
    {
        IsPlayingPreview = true;

        var options = new AnimationOptions
        {
            DurationSeconds = Math.Clamp(DisplayDurationSeconds, 3d, 10d),
            BounceStrength = Math.Clamp(BounceStrength, 0d, 0.5d),
            RippleIntensity = Math.Clamp(RippleIntensity, 0d, 2d),
            RippleSpread = Math.Clamp(RippleSpread, 0.5d, 1.5d),
        };

        try
        {
            switch (SelectedPreviewModeIndex)
            {
                case 1:
                    await _hud.ShowAndPlayAsync(PreviewSample, HudPlayMode.PowerSaver, options);
                    break;
                case 2:
                    await _hud.ShowSimpleAsync(PreviewSample, options);
                    break;
                default:
                    await _hud.ShowAndPlayAsync(PreviewSample, HudPlayMode.Charge, options);
                    break;
            }
        }
        finally
        {
            IsPlayingPreview = false;
        }
    }
}
