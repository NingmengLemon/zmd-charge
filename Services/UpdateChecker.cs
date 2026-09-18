using System.Reflection;
using System.Text.Json;

namespace EndfieldCharge.Services;

public static class UpdateChecker
{
    /// <summary>
    /// 目标仓库由编译期元数据决定（csproj 的 UpdateRepoOwner / UpdateRepoName，
    /// CI 用 github.repository 注入）。本地构建退回 csproj 里的默认值。
    /// </summary>
    private static readonly string RepoOwner = ReadMetadata("UpdateRepoOwner", "NingmengLemon");
    private static readonly string RepoName = ReadMetadata("UpdateRepoName", "zmd-charge");

    private static readonly string ReleasesUrl =
        $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    private static readonly HttpClient Client = new();

    static UpdateChecker()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        Client.DefaultRequestHeaders.UserAgent.ParseAdd($"EndfieldCharge/{version}");
        Client.Timeout = TimeSpan.FromSeconds(8);
    }

    private static string ReadMetadata(string key, string fallback)
    {
        foreach (var attr in Assembly.GetExecutingAssembly()
                     .GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (attr.Key == key && !string.IsNullOrWhiteSpace(attr.Value))
                return attr.Value!;
        }
        return fallback;
    }

    /// <summary>
    /// 检查 GitHub Releases 是否有新版本。
    /// 返回 (hasUpdate, latestVersion, downloadUrl)。
    /// 异常时直接抛出，由调用方处理。
    /// </summary>
    public static async Task<(bool HasUpdate, string? Version, string? Url)> CheckAsync()
    {
        using var response = await Client.GetAsync(ReleasesUrl);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "0.0.0";
        var current = Assembly.GetExecutingAssembly().GetName().Version;
        var latest = ParseVersion(tag);

        var hasUpdate = latest is not null && current is not null && latest > current;

        string? downloadUrl = null;
        if (root.TryGetProperty("html_url", out var html))
            downloadUrl = html.GetString();

        // 去掉 tag 前缀 v
        var display = tag.TrimStart('v');

        return (hasUpdate, display, downloadUrl);
    }

    /// <summary>把 tag（v1.2.3 / 1.2.3-dev）解析成可比较的 Version；解析不出返回 null。</summary>
    internal static Version? ParseVersion(string tag)
    {
        var v = tag.TrimStart('v');
        var parts = v.Split('-')[0].Split('.');
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], out int major) &&
            int.TryParse(parts[1], out int minor))
        {
            int build = parts.Length > 2 && int.TryParse(parts[2], out int b) ? b : 0;
            return new Version(major, minor, build);
        }
        return null;
    }
}
