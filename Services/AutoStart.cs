namespace EndfieldCharge.Services;

/// <summary>
/// 开机自启的门面。两个平台各自写各自的用户级位置，都不需要管理员权限：
///   Windows：HKCU\Software\Microsoft\Windows\CurrentVersion\Run
///   Linux  ：XDG autostart（~/.config/autostart/*.desktop）
/// </summary>
public static class AutoStart
{
    /// <summary>当前是否已启用开机自启。</summary>
    public static bool IsEnabled() => OperatingSystem.IsWindows()
        ? Windows.WindowsAutoStart.IsEnabled()
        : Linux.LinuxAutoStart.IsEnabled();

    /// <summary>启用开机自启。</summary>
    public static void Enable(string exePath)
    {
        if (OperatingSystem.IsWindows())
            Windows.WindowsAutoStart.Enable(exePath);
        else
            Linux.LinuxAutoStart.Enable(exePath);
    }

    /// <summary>禁用开机自启。</summary>
    public static void Disable()
    {
        if (OperatingSystem.IsWindows())
            Windows.WindowsAutoStart.Disable();
        else
            Linux.LinuxAutoStart.Disable();
    }

    /// <summary>当前进程的可执行文件完整路径（自启用）。</summary>
    public static string CurrentExePath =>
        Environment.ProcessPath
        ?? Path.Combine(AppContext.BaseDirectory, ExeFileName);

    private static string ExeFileName => OperatingSystem.IsWindows() ? "EndfieldCharge.exe" : "EndfieldCharge";
}
