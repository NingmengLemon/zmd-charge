using Avalonia;

namespace EndfieldCharge;

internal class Program
{
    private const string SingleInstanceMutexName = @"Local\EndfieldCharge_SingleInstance_7C1D";

    /// <summary>第二个实例用它请求已有实例弹一次 HUD。没有这条通路时，
    /// 重复启动只会静默退出，用户完全不知道程序在不在跑。</summary>
    internal const string ShowHudEventName = @"Local\EndfieldCharge_ShowHud_7C1D";

    [STAThread]
    public static void Main(string[] args)
    {
        // 只允许一个实例常驻；已运行时请已有实例弹一次 HUD，然后静默退出
        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);

        if (!createdNew)
        {
            RequestExistingInstanceShowHud();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        GC.KeepAlive(mutex);
    }

    private static void RequestExistingInstanceShowHud()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowHudEventName, out var show))
            {
                using (show)
                    show.Set();
            }
        }
        catch
        {
            // 拿不到事件就保持原来的"静默退出"
        }
    }

    /// <summary>Avalonia 配置入口，设计器也会用到，勿删。</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
