using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EndfieldCharge.Views;

namespace EndfieldCharge.Settings;

/// <summary>
/// 设置窗。逻辑全在 SettingsViewModel 里，这里只留视图自己的事：
/// 窗口图标、构造 ViewModel、把「已保存」转交给 App、关闭时解订阅。
///
/// CA1001：本类持有可释放的 ViewModel，但释放时机是窗口关闭（OnClosed），
/// 不是 IDisposable。Avalonia 的 Window 不实现 IDisposable，
/// 自行实现会与未来可能的基类实现打架。
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "释放统一走 Window.OnClosed，这是框架给的生命周期钩子。")]
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(AppSettings settings, IHudPreview hud, string initialTab = "General")
    {
        InitializeComponent();

        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://EndfieldCharge/Assets/tray_bolt.png"));
            Icon = new WindowIcon(new Bitmap(stream));
        }
        catch
        {
            // 图标加载失败不影响使用
        }

        _viewModel = new SettingsViewModel(hud, () => this, settings, CollectMonitors(), initialTab);
        _viewModel.Saved += OnSaved;
        DataContext = _viewModel;
    }

    /// <summary>显示器列表来自窗口（Screens 是 TopLevel 的属性），ViewModel 只收数据。</summary>
    private MonitorInfo[] CollectMonitors()
    {
        var screens = Screens.All;
        var result = new MonitorInfo[screens.Count];
        for (int i = 0; i < screens.Count; i++)
            result[i] = new MonitorInfo(i, screens[i].IsPrimary);
        return result;
    }

    private void OnSaved(object? sender, AppSettings settings)
    {
        if (Application.Current is App app)
            app.OnSettingsChanged(settings);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Saved -= OnSaved;
        _viewModel.Dispose();
        base.OnClosed(e);
    }
}
