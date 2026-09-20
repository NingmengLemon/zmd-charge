using System.Net.Sockets;

namespace EndfieldCharge.Services.Linux;

/// <summary>
/// Linux 的唤醒通路：Unix 域套接字。
/// 第二个实例连上、立刻断开；常驻实例 accept 之后弹 HUD。
///
/// 套接字路径优先放在 $XDG_RUNTIME_DIR（每个用户私有、重启即清），
/// 没有就退回临时目录并带上用户名，避免多用户互相踩。
/// </summary>
internal sealed class LinuxShowHudChannel : ShowHudChannel
{
    private Socket? _listener;
    private Thread? _thread;
    private volatile bool _stopping;

    internal static string SocketPath { get; } = BuildSocketPath();

    private static string BuildSocketPath()
    {
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(runtimeDir) && Directory.Exists(runtimeDir))
            return Path.Combine(runtimeDir, "endfieldcharge.sock");

        return Path.Combine(Path.GetTempPath(), $"endfieldcharge-{Environment.UserName}.sock");
    }

    internal override void StartListening(Action onRequest)
    {
        try
        {
            // 上一次异常退出可能留下套接字文件，先清掉（Bind 对已存在的路径会失败）
            if (File.Exists(SocketPath))
                File.Delete(SocketPath);

            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
            _listener.Listen(backlog: 4);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            _listener?.Dispose();
            _listener = null;
            return;
        }

        _thread = new Thread(() =>
        {
            while (!_stopping)
            {
                Socket connection;
                try
                {
                    connection = _listener!.Accept();
                }
                catch
                {
                    // 监听套接字被 Dispose 关掉时会走到这里，正常退出
                    return;
                }

                connection.Dispose();

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

        try
        {
            _listener?.Close();
        }
        catch
        {
            // 关闭失败无所谓，下面还有 Dispose
        }

        _listener?.Dispose();
        _listener = null;

        _thread?.Join(TimeSpan.FromSeconds(1));
        _thread = null;

        try
        {
            if (File.Exists(SocketPath))
                File.Delete(SocketPath);
        }
        catch
        {
            // 清理失败留给下次启动时的 File.Delete
        }
    }

    /// <summary>第二个实例调用。连不上（常驻实例不在）会抛 SocketException，由调用方吞掉。</summary>
    internal static void Signal()
    {
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        client.Connect(new UnixDomainSocketEndPoint(SocketPath));
    }
}
