using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EndfieldCharge.Services.Windows;

/// <summary>
/// 电源来源（交流 / 电池）变化监听。
///
/// 主路径：RegisterPowerSettingNotification 订阅 GUID_ACDC_POWER_SOURCE，
///         由一个后台线程上的 message-only 隐藏窗口接收 WM_POWERBROADCAST。
/// 兜底  ：低频轮询（部分机型/电源管理驱动不派发通知），开销可忽略。
///
/// 注意：事件在后台线程上触发，订阅方需自行切回 UI 线程。
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsPowerWatcher : IPowerWatcher
{
    private const string ClassName = "EndfieldCharge_PowerMsgWindow";
    private const uint WmDestroy = 0x0002;

    /// <summary>24H2（build 26100）起节能模式取代省电模式：
    /// 只有 GUID_ENERGY_SAVER_STATUS 会推送真实状态，老 GUID_POWER_SAVING_STATUS
    /// 与 SystemStatusFlag 均不再反映该开关（25H2 实测）。按版本分流，避免两个
    /// GUID 的注册回执（各带一次当前状态）在启动时互相打架、误报状态变化。</summary>
    private static readonly bool UseEnergySaverGuid =
        Environment.OSVersion.Version.Build >= 26100;

    /// <summary>轮询兜底间隔。</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 状态变化的确认延迟。
    /// Windows 在电池充满、充电器握手、固件上报抖动时会瞬间发一次
    /// "切到电池供电"(DC) 的通知，几秒内又回到 AC。若不确认就采信，
    /// 轮询读回 AC 时就会被当成一次真实的"拔掉→插上"，凭空多弹一次 HUD。
    /// 因此两个方向的变化都要复读确认，确认不符就回滚。
    /// </summary>
    private static readonly TimeSpan ChangeConfirmDelay = TimeSpan.FromMilliseconds(400);

    private readonly PowerNative.WndProcDelegate _wndProc;

    /// <summary>状态串行化：消息窗线程（WndProc）与轮询 Timer 线程都会读写下面的状态。</summary>
    private readonly object _gate = new();

    private volatile Thread? _thread;
    private volatile uint _threadId;
    private IntPtr _hwnd;
    private IntPtr _acdcNotify;
    private IntPtr _saverNotify;
    private IntPtr _energySaverNotify;
    private bool? _lastAcOnline;
    private bool? _lastSaverEnabled;
    private bool _initialized;
    private int _confirmSeq;
    private volatile bool _stopping;
    private bool _disposed;

    public WindowsPowerWatcher()
    {
        // 保持委托存活，防止被 GC 回收后 WndProc 崩溃
        _wndProc = WndProc;
    }

    /// <summary>电源来源变化。参数为当前是否交流电供电（true=已插电）。</summary>
    public event EventHandler<bool>? PowerSourceChanged;

    /// <summary>省电模式开关变化。参数为省电模式是否开启（true=已开启）。</summary>
    public event EventHandler<bool>? PowerSavingChanged;

    /// <summary>已启动并持有初始状态。</summary>
    public bool IsRunning => _thread is { IsAlive: true };

    public void Start()
    {
        if (_thread is not null)
            return;

        _stopping = false;
        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "PowerWatcher",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Stop()
    {
        _stopping = true;

        if (_threadId != 0)
            PowerNative.PostThreadMessageW(_threadId, PowerNative.WmQuit, IntPtr.Zero, IntPtr.Zero);

        // 消息循环退出时会自行清理窗口与通知句柄
        _thread?.Join(TimeSpan.FromSeconds(3));
        _thread = null;
        _threadId = 0;
    }

    private void MessageLoop()
    {
        _threadId = PowerNative.GetCurrentThreadId();

        // ---- 注册窗口类 ----
        // 类名以 IntPtr 传进 WNDCLASSEXW（原因见 PowerNative.WndClassEx 的注释）。
        // RegisterClassExW 会把类名拷进系统内部，所以注册完就能释放这块内存。
        var classNamePtr = Marshal.StringToHGlobalUni(ClassName);
        try
        {
            var wcex = new PowerNative.WndClassEx
            {
                CbSize = (uint)Marshal.SizeOf<PowerNative.WndClassEx>(),
                LpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                HInstance = PowerNative.GetModuleHandleW(null),
                LpszClassName = classNamePtr,
            };
            PowerNative.RegisterClassExW(ref wcex);
        }
        finally
        {
            Marshal.FreeHGlobal(classNamePtr);
        }

        // ---- message-only 窗口：不可见、不进任务栏、只收消息 ----
        _hwnd = PowerNative.CreateWindowExW(
            0, ClassName, null, 0,
            0, 0, 0, 0,
            PowerNative.HwndMessage,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        if (_hwnd != IntPtr.Zero)
        {
            var guid = PowerNative.GuidAcdcPowerSource;
            _acdcNotify = PowerNative.RegisterPowerSettingNotification(
                _hwnd, ref guid, PowerNative.DeviceNotifyWindowHandle);

            var saverGuid = PowerNative.GuidPowerSavingStatus;
            _saverNotify = PowerNative.RegisterPowerSettingNotification(
                _hwnd, ref saverGuid, PowerNative.DeviceNotifyWindowHandle);

            var esGuid = PowerNative.GuidEnergySaverStatus;
            _energySaverNotify = PowerNative.RegisterPowerSettingNotification(
                _hwnd, ref esGuid, PowerNative.DeviceNotifyWindowHandle);

            Logger.Info($"PowerWatcher: hwnd=0x{_hwnd.ToInt64():X}, acdcNotify=0x{_acdcNotify.ToInt64():X}, saverNotify=0x{_saverNotify.ToInt64():X}, esNotify=0x{_energySaverNotify.ToInt64():X}");
        }
        else
        {
            Logger.Warn("PowerWatcher: message window create failed, only polling available");
        }

        // ---- 记录初始状态：启动时已插电/已开省电则不弹 ----
        // 必须注册通知之后才能开始收事件，否则会在"还不知道当前状态"时
        // 就被某个事件拽到错误的初值上、然后下一次轮询又把它"修正"成真实值，
        // 看起来就像发生了一次状态变化 → 误触 HUD。
        if (PowerNative.TryGetAcOnline(out bool ac))
        {
            lock (_gate)
            {
                _lastAcOnline = ac;
                _initialized = true;
            }
        }
        if (PowerNative.TryGetPowerSavingStatus(out bool saver))
        {
            lock (_gate)
            {
                _lastSaverEnabled = saver;
            }
            Logger.Info($"PowerWatcher: initial saver={saver}");
        }

        // ---- 轮询兜底定时器 ----
        using var pollTimer = new Timer(_ => PollOnce(), null, PollInterval, PollInterval);

        // ---- 消息循环 ----
        while (PowerNative.GetMessageW(out var msg, IntPtr.Zero, 0, 0))
        {
            PowerNative.TranslateMessage(ref msg);
            PowerNative.DispatchMessageW(ref msg);
        }

        // ---- 线程内清理（窗口/通知必须在此线程销毁）----
        if (_acdcNotify != IntPtr.Zero)
        {
            PowerNative.UnregisterPowerSettingNotification(_acdcNotify);
            _acdcNotify = IntPtr.Zero;
        }
        if (_saverNotify != IntPtr.Zero)
        {
            PowerNative.UnregisterPowerSettingNotification(_saverNotify);
            _saverNotify = IntPtr.Zero;
        }
        if (_energySaverNotify != IntPtr.Zero)
        {
            PowerNative.UnregisterPowerSettingNotification(_energySaverNotify);
            _energySaverNotify = IntPtr.Zero;
        }
        if (_hwnd != IntPtr.Zero)
        {
            PowerNative.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
    }

    private void PollOnce()
    {
        if (_stopping) return;
        if (!PowerNative.TryGetAcOnline(out bool ac)) return;
        TraceEvent($"PollOnce ac={ac}");
        RaiseIfChanged(ac);

        // 省电模式轮询兜底（部分机型不派发 GUID_POWER_SAVING_STATUS 通知）
        if (PowerNative.TryGetPowerSavingStatus(out bool saver))
        {
            TraceEvent($"PollOnce saver={saver}");
            RaiseSaverIfChanged(saver);
        }
    }

    private void RaiseIfChanged(bool acOnline)
    {
        bool? previous = null;
        bool? traceLast;
        bool traceInit;
        bool raise = false;
        int seq = 0;

        lock (_gate)
        {
            traceLast = _lastAcOnline;
            traceInit = _initialized;

            // 第一次读到真实状态前，所有事件都吞掉，避免把"未知"误当 DC →
            // 随后读到真实 AC 时被当成状态变化。
            if (!_initialized)
            {
                _lastAcOnline = acOnline;
                _initialized = true;
            }
            else if (_lastAcOnline != acOnline)
            {
                previous = _lastAcOnline;
                _lastAcOnline = acOnline;
                seq = ++_confirmSeq;
                raise = true;
            }
        }

        TraceEvent($"RaiseIfChanged(ac={acOnline}), last={traceLast}, init={traceInit}");

        // 两个方向的变化都延迟复读一次，滤掉电源状态的瞬时抖动
        if (raise)
            _ = ConfirmChangeAsync(previous, acOnline, seq);
    }

    private async Task ConfirmChangeAsync(bool? previous, bool candidate, int seq)
    {
        try
        {
            await Task.Delay(ChangeConfirmDelay);
        }
        catch
        {
            return;
        }

        if (_stopping || !PowerNative.TryGetAcOnline(out bool current))
            return;

        lock (_gate)
        {
            // 期间又有更新的变化 → 本次确认作废
            if (seq != _confirmSeq)
                return;

            if (current != candidate)
            {
                // 抖动：回滚，避免后续读回真实值时被当成又一次变化
                _lastAcOnline = previous;
                return;
            }
        }

        // 两个方向都上报，由订阅方决定弹什么（插电=完整三态，拔电=简化电量胶囊）
        PowerSourceChanged?.Invoke(this, candidate);
    }

    /// <summary>
    /// 省电模式状态变化：无确认延迟——开/关省电是用户或系统的明确动作，
    /// 且轮询与通知双路径都以"与上次不同"为闸，不会重复触发。
    /// </summary>
    private void RaiseSaverIfChanged(bool enabled)
    {
        bool raise = false;

        lock (_gate)
        {
            // 第一次读到真实状态前只记录，不上报（启动时已开省电不弹）
            if (_lastSaverEnabled is null)
            {
                _lastSaverEnabled = enabled;
            }
            else if (_lastSaverEnabled != enabled)
            {
                _lastSaverEnabled = enabled;
                raise = true;
            }
        }

        if (!raise)
            return;

        TraceEvent($"PowerSavingChanged(enabled={enabled})");
        Logger.Info($"PowerWatcher: power saving {(enabled ? "ON" : "OFF")}");
        PowerSavingChanged?.Invoke(this, enabled);
    }

    /// <summary>--power-log 开关。启动时解析一次，Release 构建同样生效。</summary>
    private static readonly bool PowerLogEnabled =
        Array.Exists(Environment.GetCommandLineArgs(), a => a == "--power-log");

    /// <summary>电源事件日志的字节上限，超过后停止追加，避免长时间挂机写满磁盘。</summary>
    private const long PowerLogMaxBytes = 2 * 1024 * 1024;

    private static void TraceEvent(string msg)
    {
        if (!PowerLogEnabled)
            return;
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "power-log.txt");
            var info = new FileInfo(path);
            if (info.Exists && info.Length > PowerLogMaxBytes)
                return;

            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch
        {
            // 忽略日志失败
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == PowerNative.WmPowerBroadcast)
        {
            int w = wParam.ToInt32();

            if (w == PowerNative.PbtPowerSettingChange && lParam != IntPtr.Zero)
            {
                try
                {
                    var setting = Marshal.PtrToStructure<PowerNative.PowerBroadcastSetting>(lParam);
                    if (setting.PowerSetting == PowerNative.GuidAcdcPowerSource)
                    {
                        RaiseIfChanged(setting.Data == PowerNative.AcPowerSource);
                    }
                    else if (setting.PowerSetting == PowerNative.GuidPowerSavingStatus
                             && !UseEnergySaverGuid)
                    {
                        RaiseSaverIfChanged(setting.Data == 1);
                    }
                    else if (setting.PowerSetting == PowerNative.GuidEnergySaverStatus
                             && UseEnergySaverGuid)
                    {
                        // 24H2+ 节能模式：0=关, 1=标准, 2=高节能（非 0 即开启）
                        RaiseSaverIfChanged(setting.Data != 0);
                    }
                }
                catch
                {
                    // 结构解析失败则忽略，轮询兜底会补上
                }
            }
            else if (w == PowerNative.PbtApmPowerStatusChange)
            {
                // 通用电源状态变化：重新读一次真实状态
                if (PowerNative.TryGetAcOnline(out bool ac))
                    RaiseIfChanged(ac);
                if (PowerNative.TryGetPowerSavingStatus(out bool saver))
                    RaiseSaverIfChanged(saver);
            }
        }
        else if (msg == WmDestroy)
        {
            return IntPtr.Zero;
        }

        return PowerNative.DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
    }
}
