using CommunityToolkit.Mvvm.ComponentModel;
using EndfieldCharge.Settings;

namespace EndfieldCharge;

/// <summary>
/// 本地化：支持运行时语言切换。
/// 优先使用 settings.Language，其次系统 UI 语言。
///
/// 之所以从静态类改成「可观察单例」（ObservableObject + Current）：
/// 设置窗改 MVVM 后，几十条文案要能通过 {Binding L.xxx} 绑定，
/// 而绑定需要一个带 INotifyPropertyChanged 的**实例**。
/// 这样文案仍然只有这一份定义，不必在 ViewModel 里再抄一遍属性名。
/// 语言切换时 UseSettings 会一次性通知所有属性，绑定自动更新。
/// </summary>
public sealed class Localization : ObservableObject
{
    /// <summary>全局唯一实例。应用内所有取文案的地方都走它。</summary>
    public static Localization Current { get; } = new();

    private AppSettings? _settings;

    /// <summary>切换设置（含语言）。会通知所有绑定刷新。</summary>
    public void UseSettings(AppSettings settings)
    {
        _settings = settings;
        NotifyAll();
    }

    /// <summary>通知所有属性都已变化。空属性名按约定表示「全部」。</summary>
    public void NotifyAll() => OnPropertyChanged(string.Empty);

    private bool IsChinese
    {
        get
        {
            if (_settings?.Language is not null && _settings.Language != "auto")
                return _settings.Language.StartsWith("zh", StringComparison.Ordinal);
            return Thread.CurrentThread.CurrentUICulture.Name.StartsWith("zh", StringComparison.Ordinal);
        }
    }

    // ---- HUD ----
    public string TagLine => IsChinese ? "/// 超充模式" : "/// SUPER CHARGE MODE";
    public string TitleMode => IsChinese ? "超充模式" : "Super Charge Mode";
    public string TagLineSaver => IsChinese ? "/// 省电模式" : "/// POWER SAVING MODE";
    public string TitleSaver => IsChinese ? "省电模式" : "Power Saving Mode";

    // ---- 托盘菜单 ----
    public string PreviewHud => IsChinese ? "预览电量 HUD" : "Preview Power HUD";
    public string AutoStart => IsChinese ? "开机自启" : "Auto Start";
    public string Settings => IsChinese ? "设置" : "Settings";
    public string CheckUpdate => IsChinese ? "检查更新" : "Check for Updates";
    public string Exit => IsChinese ? "退出" : "Exit";
    public string TrayTooltip => IsChinese ? "EndfieldCharge · 电量 HUD" : "EndfieldCharge · Power HUD";

    // ---- 设置窗口 ----
    public string SettingsTitle => IsChinese ? "设置" : "Settings";
    public string TabGeneral => IsChinese ? "通用" : "General";
    public string TabNotifications => IsChinese ? "通知" : "Notifications";
    public string TabAbout => IsChinese ? "关于" : "About";
    public string LabelScale => IsChinese ? "全局缩放" : "Global Scale";
    public string LabelDuration => IsChinese ? "显示时长（秒）" : "Display Duration (s)";
    public string LabelPosition => IsChinese ? "HUD 位置" : "HUD Position";
    public string LabelMonitor => IsChinese ? "显示器" : "Monitor";
    public string LabelLanguage => IsChinese ? "语言" : "Language";
    public string ValueAuto => IsChinese ? "自动" : "Auto";
    public string ValueChinese => IsChinese ? "中文" : "Chinese";
    public string ValueEnglish => IsChinese ? "英文" : "English";
    public string PosTopCenter => IsChinese ? "顶部居中" : "Top Center";
    public string PosTopRight => IsChinese ? "顶部靠右" : "Top Right";
    public string PosTopLeft => IsChinese ? "顶部靠左" : "Top Left";
    public string LabelLowBattery => IsChinese ? "低电量提醒阈值" : "Low Battery Alert Threshold";
    public string LabelLowBatteryEnable => IsChinese ? "启用低电量提醒" : "Enable Low Battery Alert";
    public string LabelFullChargeEnable => IsChinese ? "充满时提醒" : "Alert When Fully Charged";

    // ---- 设置窗口段落标题 ----
    public string SectionDisplay => IsChinese ? "显示" : "Display";
    public string SectionPosition => IsChinese ? "位置与语言" : "Position & Language";
    public string SectionStartup => IsChinese ? "启动" : "Startup";
    public string SectionAlertSettings => IsChinese ? "提醒设置" : "Alert Settings";
    public string DescAutoStart => IsChinese ? "登录 Windows 时自动启动" : "Auto start on Windows login";
    public string DescLowBatteryAlert => IsChinese ? "电量低于阈值时弹窗提醒" : "Alert when battery drops below threshold";
    public string DescFullChargeAlert => IsChinese ? "电池充满后弹窗通知" : "Notify when battery is fully charged";

    // ---- 关于 ----
    public string LabelVersion => IsChinese ? "版本" : "Version";
    public string LabelAuthor => IsChinese ? "作者" : "Author";
    public string AboutSubtitle => IsChinese ? "终末地风格电量 HUD" : "Endfield-style Power HUD";

    // ---- 显示器 ----
    public string MonitorName(int index, bool isPrimary) => IsChinese
        ? isPrimary ? $"显示器 {index + 1}（主）" : $"显示器 {index + 1}"
        : isPrimary ? $"Monitor {index + 1} (Primary)" : $"Monitor {index + 1}";
    public string MonitorPrimaryDefault => IsChinese ? "主显示器（默认）" : "Primary Monitor (Default)";

    // ---- 动画页 ----
    public string TabAnimation => IsChinese ? "动画" : "Animation";
    public string SectionAnimParams => IsChinese ? "动画参数" : "Animation Parameters";
    public string SectionPreview => IsChinese ? "预览" : "Preview";
    public string LabelBounce => IsChinese ? "回弹强度" : "Bounce Strength";
    public string LabelRippleIntensity => IsChinese ? "波纹强度" : "Ripple Intensity";
    public string LabelRippleSpread => IsChinese ? "波纹幅度" : "Ripple Spread";
    public string LabelPlayMode => IsChinese ? "播放模式" : "Play Mode";
    public string ModePlug => IsChinese ? "插电（完整三态）" : "Plug In (Full Sequence)";
    public string ModeUnplug => IsChinese ? "拔电（简化胶囊）" : "Unplug (Simple Capsule)";
    public string ModeSaver => IsChinese ? "省电模式（完整三态）" : "Battery Saver (Full Sequence)";

    // ---- 通知 ----
    public string LabelPowerSaverNotify => IsChinese ? "省电模式切换提示" : "Battery Saver Toggle Notify";
    public string PowerSaverNotifyDesc => IsChinese
        ? "开启 / 关闭系统省电模式时显示 HUD"
        : "Show HUD when battery saver is toggled";
    public string LabelUpdateCheck => IsChinese ? "自动检查更新" : "Auto Check for Updates";
    public string LabelAutoStart => IsChinese ? "开机自启" : "Auto Start";
    public string BtnCheckUpdate => IsChinese ? "检查更新" : "Check Now";
    public string BtnSave => IsChinese ? "保存" : "Save";
    public string SavedToast => IsChinese ? "设置已保存" : "Settings saved";

    // ---- 提醒 ----
    public string LowBatteryTitle => IsChinese ? "电量不足" : "Low Battery";
    public string LowBatteryMsg(int pct) => IsChinese
        ? $"电量仅剩 {pct}%，请及时充电"
        : $"Battery at {pct}%, please charge soon";
    public string FullChargeTitle => IsChinese ? "已充满" : "Fully Charged";
    public string FullChargeMsg => IsChinese ? "电池已充满，可以拔掉电源了" : "Battery is fully charged, you can unplug now";

    // ---- 更新 ----
    public string UpdateTitle => IsChinese ? "发现新版本" : "Update Available";
    public string UpdateMsg(string ver) => IsChinese
        ? $"EndfieldCharge {ver} 已发布，是否前往下载？"
        : $"EndfieldCharge {ver} is available. Download now?";
    public string UpToDate => IsChinese ? "已是最新版本" : "You're up to date";
    public string UpdateCheckFailed => IsChinese ? "检查更新失败" : "Update check failed";
    public string BtnDownload => IsChinese ? "下载" : "Download";
    public string BtnCancel => IsChinese ? "取消" : "Cancel";

    // ---- 预览 ----
    public string PreviewTitle => IsChinese ? "动画预览" : "Animation Preview";
    public string BtnPlay => IsChinese ? "播放" : "Play";

    // ---- 字体 ----
    public string FontSectionTitle => IsChinese ? "字体" : "Font";
    public string FontDesc => IsChinese
        ? "HUD 数字与英文字体使用 Inter Medium，可获得更清晰的渲染效果"
        : "HUD uses Inter Medium for digital and English text for sharper rendering";
    public string BtnInstallFont => IsChinese ? "安装 Inter 字体" : "Install Inter Font";
    public string FontInstalling => IsChinese ? "正在打开字体下载页…" : "Opening font download page...";
    public string FontInstalled => IsChinese ? "下载后双击 Inter.ttf 文件即可安装" : "Download and double-click Inter.ttf to install";
}
