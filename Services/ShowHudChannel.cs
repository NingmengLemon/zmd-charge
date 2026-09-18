using EndfieldCharge.Services.Linux;
using EndfieldCharge.Services.Windows;

namespace EndfieldCharge.Services;

/// <summary>
/// 「第二个实例请求已有实例弹一次 HUD」的通路。
/// 没有这条通路时，重复启动只会静默退出，用户完全不知道程序在不在跑。
///
/// 平台差异（实测依据：在 Debian 13 + .NET 10 上跑过，见 README 的平台差异说明）：
///   Windows：命名 EventWaitHandle
///   Linux  ：Unix 域套接字。命名事件对象在 Linux 上会抛
///            PlatformNotSupportedException（命名 Mutex 倒是两个平台都支持，
///            所以单实例判定不需要分平台）
/// </summary>
internal abstract class ShowHudChannel : IDisposable
{
    /// <summary>作为常驻实例开始监听。onRequest 在后台线程触发。</summary>
    internal abstract void StartListening(Action onRequest);

    public abstract void Dispose();

    internal static ShowHudChannel Create() =>
        OperatingSystem.IsWindows() ? new WindowsShowHudChannel() : new LinuxShowHudChannel();

    /// <summary>第二个实例调用：请求已有实例弹一次 HUD。拿不到通路就静默返回。</summary>
    internal static void TrySignalExistingInstance()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                WindowsShowHudChannel.Signal();
            else
                LinuxShowHudChannel.Signal();
        }
        catch
        {
            // 保持原来的"静默退出"语义
        }
    }
}
