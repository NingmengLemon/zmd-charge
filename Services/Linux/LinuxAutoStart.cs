namespace EndfieldCharge.Services.Linux;

/// <summary>
/// Linux 开机自启：XDG autostart。
/// 写 ~/.config/autostart/endfieldcharge.desktop，桌面环境登录时由各家的 autostart
/// 实现读取，不需要额外权限，也不用碰 systemd。
///
/// 注意：ApplicationData 在 Linux 上映射到 $XDG_CONFIG_HOME（默认 ~/.config），
/// 所以这里不需要自己解析 XDG 变量。
/// </summary>
internal static class LinuxAutoStart
{
    private const string FileName = "endfieldcharge.desktop";

    private static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "autostart");

    private static string FilePath => Path.Combine(Directory, FileName);

    internal static bool IsEnabled()
    {
        try
        {
            return File.Exists(FilePath);
        }
        catch
        {
            return false;
        }
    }

    internal static void Enable(string exePath)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            // 按 Desktop Entry 规范：Exec 里含空格的路径要用双引号包起来
            File.WriteAllText(FilePath,
                $"""
                [Desktop Entry]
                Type=Application
                Name=EndfieldCharge
                Comment=Endfield-style Power HUD
                Exec="{exePath}"
                Terminal=false
                X-GNOME-Autostart-enabled=true
                """ + "\n");
        }
        catch
        {
            // 写失败静默，与 Windows 侧一致（开关状态由调用方自行处理）
        }
    }

    internal static void Disable()
    {
        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
        catch
        {
            // 忽略删除失败
        }
    }
}
