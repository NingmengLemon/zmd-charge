using EndfieldCharge.Services.Linux;
using EndfieldCharge.Services.Windows;

namespace EndfieldCharge.Services;

/// <summary>
/// 电源状态变化监听。事件在后台线程触发，订阅方需自行切回 UI 线程。
/// </summary>
internal interface IPowerWatcher : IDisposable
{
    /// <summary>电源来源变化。参数为当前是否交流电供电（true=已插电）。</summary>
    event EventHandler<bool>? PowerSourceChanged;

    /// <summary>省电模式开关变化。Windows 上可用；Linux 上永远不会触发（见 LinuxPowerWatcher）。</summary>
    event EventHandler<bool>? PowerSavingChanged;

    void Start();
}

/// <summary>按操作系统挑选电源监听实现。</summary>
internal static class PowerWatcherFactory
{
    internal static IPowerWatcher Create() =>
        OperatingSystem.IsWindows() ? new WindowsPowerWatcher() : new LinuxPowerWatcher();
}
