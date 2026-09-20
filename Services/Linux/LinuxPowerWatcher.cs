namespace EndfieldCharge.Services.Linux;

/// <summary>
/// Linux 电源变化监听：纯轮询。
///
/// 为什么不用 UPower 的 D-Bus 信号：那会多一个运行期依赖（虽然桌面发行版上基本都有 UPower），
/// 而 sysfs 轮询零依赖、行为可预测。代价是最多 PollInterval 的延迟，
/// 对"插拔充电器弹个 HUD"这个场景完全够用。
///
/// 与 Windows 实现不同，这里没有 400ms 去抖确认：那个去抖是为了滤掉 Windows 在满电、
/// 充电器握手时瞬时误报的 DC 通知，而 sysfs 是被动读值，不存在这种抖动。
///
/// 省电模式：Linux 上没有统一接口（GNOME / KDE / power-profiles-daemon 各不相同），
/// 所以 PowerSavingChanged 永远不会触发，见下面显式的空事件访问器。
/// </summary>
internal sealed class LinuxPowerWatcher : IPowerWatcher
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private Timer? _timer;
    private bool? _lastAcOnline;
    private volatile bool _disposed;

    public event EventHandler<bool>? PowerSourceChanged;

    /// <summary>
    /// Linux 上不实现省电模式提示（没有跨桌面环境的统一接口）。
    /// 显式写成空访问器而不是留一个从不触发的字段：后者会触发 CS0067，
    /// 而这里需要表达的是"刻意的空实现"，不是"忘了触发"。
    /// </summary>
    public event EventHandler<bool>? PowerSavingChanged
    {
        add { }
        remove { }
    }

    public void Start()
    {
        if (_timer is not null)
            return;

        // 记录初值：启动时就已插电不该弹
        if (BatteryService.GetSnapshot() is { } snap)
            _lastAcOnline = snap.AcOnline;

        _timer = new Timer(_ => Poll(), null, PollInterval, PollInterval);
    }

    private void Poll()
    {
        if (_disposed)
            return;

        if (BatteryService.GetSnapshot() is not { } snap)
            return;

        bool? previous;
        lock (_gate)
        {
            previous = _lastAcOnline;
            _lastAcOnline = snap.AcOnline;
        }

        // 第一次读到真实状态前不报，与 Windows 实现一致
        if (previous is not null && previous != snap.AcOnline)
            PowerSourceChanged?.Invoke(this, snap.AcOnline);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer?.Dispose();
        _timer = null;
    }
}
