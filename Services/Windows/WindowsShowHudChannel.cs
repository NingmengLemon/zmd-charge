using System.Runtime.Versioning;

namespace EndfieldCharge.Services.Windows;

/// <summary>Windows 的唤醒通路：命名 EventWaitHandle。</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsShowHudChannel : ShowHudChannel
{
    private const string EventName = @"Local\EndfieldCharge_ShowHud_7C1D";

    private EventWaitHandle? _event;
    private Thread? _thread;
    private volatile bool _stopping;

    internal override void StartListening(Action onRequest)
    {
        try
        {
            _event = new EventWaitHandle(
                initialState: false, EventResetMode.AutoReset, EventName);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            return;
        }

        _thread = new Thread(() =>
        {
            while (true)
            {
                _event!.WaitOne();
                if (_stopping)
                    return;

                onRequest();
            }
        })
        {
            IsBackground = true,
            Name = "ShowHudListener",
        };
        _thread.Start();
    }

    public override void Dispose()
    {
        _stopping = true;

        // 唤醒等待中的监听线程（它是后台线程，不阻塞退出）
        _event?.Set();
        _thread?.Join(TimeSpan.FromSeconds(1));
        _thread = null;
        _event?.Dispose();
        _event = null;
    }

    /// <summary>第二个实例调用。</summary>
    internal static void Signal()
    {
        if (EventWaitHandle.TryOpenExisting(EventName, out var show))
        {
            using (show)
                show.Set();
        }
    }
}
