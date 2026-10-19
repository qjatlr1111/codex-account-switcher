using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace ClaudeAccountWidget.Services;

/// <summary>
/// Codex 위젯과 같은 저장소를 쓰므로 releases/latest가 아니라 릴리스 목록에서
/// claude-v 태그만 골라 가장 높은 버전을 찾습니다.
/// </summary>
public sealed class UpdateCheckService
{
    public const string RepositoryPath = "qjatlr1111/codex-account-switcher";
    public const string TagPrefix = "claude-v";
    public const string InstallerFileName = "ClaudeAccountSwitcher-Setup.exe";
    public const string ChecksumFileName = "SHA256SUMS.txt";

    private const string ReleasesApiUrl = $"https://api.github.com/repos/{RepositoryPath}/releases?per_page=50";
    private static readonly HttpClient Client = CreateClient();

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(ReleasesApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        JsonElement? latestRelease = null;
        Version? latestVersion = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            if (release.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean()) continue;

            var tag = release.GetProperty("tag_name").GetString() ?? string.Empty;
            if (!tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!Version.TryParse(tag[TagPrefix.Length..], out var version)) continue;

            if (latestVersion is null || version > latestVersion)
            {
                latestVersion = version;
                latestRelease = release.Clone();
            }
        }

        if (latestRelease is not { } root || latestVersion is null)
            throw new InvalidOperationException("Claude 위젯 릴리스를 찾지 못했습니다.");

        var tagName = root.GetProperty("tag_name").GetString()!;
        var releasePage = root.GetProperty("html_url").GetString();
        if (!Uri.TryCreate(releasePage, UriKind.Absolute, out var releasePageUri) ||
            releasePageUri.Scheme != Uri.UriSchemeHttps ||
            !releasePageUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("안전한 GitHub 릴리스 주소를 받지 못했습니다.");

        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        var installerUri = GetReleaseAssetUri(assets, InstallerFileName, tagName);
        var checksumUri = GetReleaseAssetUri(assets, ChecksumFileName, tagName);
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        var current = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build));

        return new UpdateCheckResult(
            current,
            latestVersion,
            tagName,
            releasePageUri,
            installerUri,
            checksumUri,
            latestVersion.CompareTo(current) > 0);
    }

    private static Uri GetReleaseAssetUri(JsonElement[] assets, string fileName, string tagName)
    {
        var asset = assets.SingleOrDefault(candidate => candidate.GetProperty("name").GetString() == fileName);
        var url = asset.ValueKind == JsonValueKind.Undefined
            ? null
            : asset.GetProperty("browser_download_url").GetString();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith(
                $"/{RepositoryPath}/releases/download/{tagName}/", StringComparison.Ordinal))
            throw new InvalidOperationException($"안전한 {fileName} 주소를 받지 못했습니다.");
        return uri;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ClaudeAccountSwitcher", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}

public sealed record UpdateCheckResult(
    Version CurrentVersion,
    Version LatestVersion,
    string LatestTagName,
    Uri ReleasePageUri,
    Uri InstallerUri,
    Uri ChecksumUri,
    bool IsUpdateAvailable);
