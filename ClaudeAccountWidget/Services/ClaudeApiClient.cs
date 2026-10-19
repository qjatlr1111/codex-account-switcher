using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ClaudeAccountWidget.Services;

public sealed record UsageWindow(double? Utilization, DateTimeOffset? ResetsAt)
{
    public int? Remaining => Utilization is { } used
        ? (int)Math.Clamp(Math.Round(100 - used), 0, 100)
        : null;
}

public sealed record UsageSnapshot(
    UsageWindow? FiveHour,
    UsageWindow? SevenDay,
    UsageWindow? SevenDayOpus,
    UsageWindow? SevenDaySonnet);

public sealed record ClaudeProfileInfo(
    string? AccountUuid,
    string? Email,
    string? DisplayName,
    string? OrganizationName,
    string? OrganizationType,
    string? RateLimitTier);

public sealed record TokenRefreshResult(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);

public sealed class ClaudeApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

/// <summary>
/// Claude Code와 같은 OAuth 토큰으로 계정·사용량을 조회합니다.
/// 비공개 엔드포인트이므로 응답 형식이 바뀌어도 앱이 멈추지 않도록 모든 필드를 선택적으로 읽습니다.
/// </summary>
public sealed class ClaudeApiClient
{
    private const string ApiBase = "https://api.anthropic.com";
    private const string OAuthBeta = "oauth-2025-04-20";
    private const string ClaudeCodeClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    private static readonly string[] TokenEndpoints =
    [
        "https://platform.claude.com/v1/oauth/token",
        "https://console.anthropic.com/v1/oauth/token"
    ];

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("claude-account-switcher/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public async Task<UsageSnapshot> GetUsageAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync("/api/oauth/usage", accessToken, cancellationToken);
        var root = document.RootElement;
        return new UsageSnapshot(
            ReadWindow(root, "five_hour"),
            ReadWindow(root, "seven_day"),
            ReadWindow(root, "seven_day_opus"),
            ReadWindow(root, "seven_day_sonnet"));
    }

    public async Task<ClaudeProfileInfo> GetProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync("/api/oauth/profile", accessToken, cancellationToken);
        var root = document.RootElement;
        var account = root.TryGetProperty("account", out var a) && a.ValueKind == JsonValueKind.Object ? a : default;
        var organization = root.TryGetProperty("organization", out var o) && o.ValueKind == JsonValueKind.Object ? o : default;

        return new ClaudeProfileInfo(
            Str(account, "uuid"),
            Str(account, "email") ?? Str(account, "email_address"),
            Str(account, "display_name") ?? Str(account, "full_name"),
            Str(organization, "name"),
            Str(organization, "organization_type"),
            Str(organization, "rate_limit_tier"));
    }

    public async Task<TokenRefreshResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var endpoint in TokenEndpoints)
        {
            try
            {
                using var response = await Http.PostAsJsonAsync(endpoint, new
                {
                    grant_type = "refresh_token",
                    refresh_token = refreshToken,
                    client_id = ClaudeCodeClientId
                }, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    lastError = new ClaudeApiException(response.StatusCode, "토큰 갱신 거부");
                    // 400/401은 refresh token 자체가 만료된 경우이므로 다른 엔드포인트를 시도해도 같습니다.
                    if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized) break;
                    continue;
                }

                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var root = document.RootElement;
                var accessToken = Str(root, "access_token")
                                  ?? throw new InvalidOperationException("새 액세스 토큰을 받지 못했습니다.");
                var expiresIn = root.TryGetProperty("expires_in", out var e) && e.TryGetInt64(out var seconds)
                    ? seconds
                    : 3600;
                return new TokenRefreshResult(
                    accessToken,
                    Str(root, "refresh_token"),
                    DateTimeOffset.UtcNow.AddSeconds(expiresIn));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                lastError = exception;
            }
        }

        throw lastError ?? new InvalidOperationException("토큰을 갱신하지 못했습니다.");
    }

    private static async Task<JsonDocument> GetJsonAsync(string path, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiBase + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("anthropic-beta", OAuthBeta);
        request.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "인증 만료",
                HttpStatusCode.Forbidden => "사용량 조회 권한 없음",
                HttpStatusCode.TooManyRequests => "조회 한도 초과 · 잠시 후 재시도",
                _ => $"Claude 응답 오류 ({(int)response.StatusCode})"
            };
            throw new ClaudeApiException(response.StatusCode, message);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(body);
    }

    private static UsageWindow? ReadWindow(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var window) || window.ValueKind != JsonValueKind.Object)
            return null;

        double? utilization = window.TryGetProperty("utilization", out var u) && u.ValueKind == JsonValueKind.Number
            ? u.GetDouble()
            : null;
        DateTimeOffset? resetsAt = window.TryGetProperty("resets_at", out var r) &&
                                   r.ValueKind == JsonValueKind.String &&
                                   DateTimeOffset.TryParse(r.GetString(), out var parsed)
            ? parsed
            : null;
        return new UsageWindow(utilization, resetsAt);
    }

    private static string? Str(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
