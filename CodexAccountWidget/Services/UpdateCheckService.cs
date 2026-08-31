using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace CodexAccountWidget.Services;

public sealed class UpdateCheckService
{
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/qjatlr1111/codex-account-switcher/releases/latest";
    private static readonly HttpClient Client = CreateClient();

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(LatestReleaseApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var tagName = root.GetProperty("tag_name").GetString()
                      ?? throw new InvalidOperationException("최신 버전 태그가 없습니다.");
        if (!Version.TryParse(tagName.TrimStart('v', 'V'), out var latestVersion))
            throw new InvalidOperationException($"알 수 없는 버전 형식입니다: {tagName}");

        var releasePage = root.GetProperty("html_url").GetString();
        if (!Uri.TryCreate(releasePage, UriKind.Absolute, out var releasePageUri) ||
            releasePageUri.Scheme != Uri.UriSchemeHttps ||
            !releasePageUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("안전한 GitHub 릴리스 주소를 받지 못했습니다.");

        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version
                             ?? new Version(0, 0, 0);
        return new UpdateCheckResult(
            currentVersion,
            latestVersion,
            tagName,
            releasePageUri,
            latestVersion.CompareTo(currentVersion) > 0);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("CodexAccountSwitcher", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}

public sealed record UpdateCheckResult(
    Version CurrentVersion,
    Version LatestVersion,
    string LatestTagName,
    Uri ReleasePageUri,
    bool IsUpdateAvailable);
