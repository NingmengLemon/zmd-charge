namespace EndfieldCharge.Services;

/// <summary>
/// 简单文件日志。写入 %TEMP%/EndfieldCharge/log-{yyyyMMdd}.txt。
/// 仅当 Enabled 为 true 时写入（由设置控制）。
/// 多线程会同时写（UI 线程 + 电源监听线程），因此加锁串行化：
/// 否则 AppendAllText 抛的共享冲突会被 catch 吞掉，表现为"日志缺行"。
/// </summary>
public static class Logger
{
    private static readonly string LogDir = Path.Combine(
        Path.GetTempPath(), "EndfieldCharge");

    /// <summary>日志保留天数，超出即删除。</summary>
    private const int RetentionDays = 7;

    /// <summary>单日文件字节上限，超出后停止追加（防止异常循环把磁盘写满）。</summary>
    private const long MaxFileBytes = 5 * 1024 * 1024;

    private static readonly object Gate = new();
    private static bool _pruned;

    public static bool Enabled { get; set; }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    /// <summary>记录异常：带完整堆栈，否则崩溃日志基本没法定位。</summary>
    public static void Error(Exception ex) => Write("ERROR", ex.ToString());

    /// <summary>
    /// 只在进程内第一次调用时记一条。用于「会被高频轮询反复触发的失败路径」：
    /// 每次都记会迅速把日志刷到上限、把真正有用的信息挤掉；完全不记又会把问题藏起来。
    /// flag 由调用方持有（通常是一个 private static int 字段）。
    /// </summary>
    public static void Once(ref int flag, string msg)
    {
        if (Interlocked.Exchange(ref flag, 1) == 0)
            Write("WARN", msg);
    }

    private static void Write(string level, string msg)
    {
        if (!Enabled)
            return;

        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(LogDir);
                PruneOnce();

                var path = Path.Combine(LogDir, $"log-{DateTime.Now:yyyyMMdd}.txt");
                var info = new FileInfo(path);
                if (info.Exists && info.Length > MaxFileBytes)
                    return;

                File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {msg}\n");
            }
            catch
            {
                // 日志写入失败忽略
            }
        }
    }

    /// <summary>每次进程生命周期内清理一次过期日志。</summary>
    private static void PruneOnce()
    {
        if (_pruned)
            return;
        _pruned = true;

        try
        {
            var cutoff = DateTime.Now.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(LogDir, "log-*.txt"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch
        {
            // 清理失败不影响正常写日志
        }
    }
}
