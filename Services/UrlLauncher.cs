using System.Diagnostics;

namespace EndfieldCharge.Services;

/// <summary>
/// 用系统默认程序打开 URL。
///
/// 这个类原来叫 Platform，和 Avalonia.Platform 命名空间撞名，逼得调用方到处写
/// Avalonia.Platform.Screen 这种全限定名。改名成 UrlLauncher 之后那些全限定名都可以去掉。
/// </summary>
internal static class UrlLauncher
{
    public static void Open(string url)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            },
        };
        p.Start();
    }
}
