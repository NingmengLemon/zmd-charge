using Avalonia;
using EndfieldCharge.Services;

namespace EndfieldCharge;

internal class Program
{
    /// <summary>
    /// 单实例判定用的命名 Mutex。
    /// 命名 Mutex 在 Windows 与 Linux 上都可用，且跨进程语义一致（Linux 上实测过
    /// createdNew 第二次为 false），所以这里不需要分平台。
    /// </summary>
    private const string SingleInstanceMutexName = @"Local\EndfieldCharge_SingleInstance_7C1D";

    [STAThread]
    public static void Main(string[] args)
    {
        // 只允许一个实例常驻；已运行时请已有实例弹一次 HUD，然后静默退出
        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);

        if (!createdNew)
        {
            ShowHudChannel.TrySignalExistingInstance();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        GC.KeepAlive(mutex);
    }

    /// <summary>Avalonia 配置入口，设计器也会用到，勿删。</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
