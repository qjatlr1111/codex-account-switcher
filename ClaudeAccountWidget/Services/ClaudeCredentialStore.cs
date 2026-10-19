using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeAccountWidget.Services;

public sealed record ClaudeCredentials(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    string? SubscriptionType,
    string? RateLimitTier)
{
    public bool IsExpired(TimeSpan margin) =>
        ExpiresAt is { } expiresAt && expiresAt - margin <= DateTimeOffset.UtcNow;
}

public sealed record ClaudeOAuthAccount(
    string? AccountUuid,
    string? Email,
    string? DisplayName,
    string? OrganizationName,
    JsonObject Raw);

/// <summary>
/// Claude Code의 .credentials.json과 .claude.json(oauthAccount)을 읽고 씁니다.
/// 알 수 없는 필드는 그대로 보존하고, 모든 쓰기는 임시 파일을 거쳐 원자적으로 교체합니다.
/// 토큰 값은 절대 로그로 남기지 않습니다.
/// </summary>
public static class ClaudeCredentialStore
{
    private const string OAuthSection = "claudeAiOauth";
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static ClaudeCredentials? TryReadCredentials(string credentialsPath)
    {
        var root = TryReadObject(credentialsPath);
        if (root?[OAuthSection] is not JsonObject oauth) return null;

        var accessToken = GetString(oauth, "accessToken");
        if (string.IsNullOrWhiteSpace(accessToken)) return null;

        DateTimeOffset? expiresAt = null;
        if (oauth["expiresAt"] is JsonValue expiresValue)
        {
            if (expiresValue.TryGetValue<long>(out var milliseconds))
                expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
            else if (expiresValue.TryGetValue<double>(out var millisecondsDouble))
                expiresAt = DateTimeOffset.FromUnixTimeMilliseconds((long)millisecondsDouble);
        }

        return new ClaudeCredentials(
            accessToken,
            GetString(oauth, "refreshToken"),
            expiresAt,
            GetString(oauth, "subscriptionType"),
            GetString(oauth, "rateLimitTier"));
    }

    public static void UpdateTokens(
        string credentialsPath,
        string accessToken,
        string? refreshToken,
        DateTimeOffset expiresAt)
    {
        var root = TryReadObject(credentialsPath)
                   ?? throw new InvalidOperationException("인증 파일을 읽지 못했습니다.");
        if (root[OAuthSection] is not JsonObject oauth)
            throw new InvalidOperationException("인증 파일에 Claude 로그인 정보가 없습니다.");

        oauth["accessToken"] = accessToken;
        if (!string.IsNullOrWhiteSpace(refreshToken)) oauth["refreshToken"] = refreshToken;
        oauth["expiresAt"] = expiresAt.ToUnixTimeMilliseconds();
        WriteAtomically(credentialsPath, root.ToJsonString(WriteOptions));
    }

    public static ClaudeOAuthAccount? TryReadOAuthAccount(string globalConfigPath)
    {
        var root = TryReadObject(globalConfigPath);
        if (root?["oauthAccount"] is not JsonObject account) return null;

        return new ClaudeOAuthAccount(
            GetString(account, "accountUuid"),
            GetString(account, "emailAddress"),
            GetString(account, "displayName"),
            GetString(account, "organizationName"),
            (JsonObject)account.DeepClone());
    }

    /// <summary>.claude.json의 다른 설정(프로젝트, MCP 등)은 그대로 두고 oauthAccount만 교체합니다.</summary>
    public static void WriteOAuthAccount(string globalConfigPath, JsonObject account)
    {
        var root = TryReadObject(globalConfigPath) ?? new JsonObject();
        root["oauthAccount"] = account.DeepClone();
        WriteAtomically(globalConfigPath, root.ToJsonString(WriteOptions));
    }

    /// <summary>인증 파일을 통째로 복사합니다. 대상이 있으면 한 개의 백업을 남길 수 있습니다.</summary>
    public static void CopyCredentials(string sourcePath, string targetPath, string? backupPath = null)
    {
        if (!File.Exists(sourcePath))
            throw new InvalidOperationException("복사할 인증 파일이 없습니다.");

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        if (backupPath is not null && File.Exists(targetPath))
            File.Copy(targetPath, backupPath, overwrite: true);

        WriteAtomically(targetPath, File.ReadAllText(sourcePath, Encoding.UTF8));
    }

    public static bool FilesMatch(string leftPath, string rightPath)
    {
        if (!File.Exists(leftPath) || !File.Exists(rightPath)) return false;
        using var left = File.OpenRead(leftPath);
        using var right = File.OpenRead(rightPath);
        return SHA256.HashData(left).SequenceEqual(SHA256.HashData(right));
    }

    /// <summary>같은 계정의 같은 로그인 세션인지(토큰 문자열 비교 없이) 확인합니다.</summary>
    public static bool SameAccessToken(string leftPath, string rightPath)
    {
        var left = TryReadCredentials(leftPath);
        var right = TryReadCredentials(rightPath);
        return left is not null && right is not null &&
               CryptographicOperations.FixedTimeEquals(
                   Encoding.UTF8.GetBytes(left.AccessToken),
                   Encoding.UTF8.GetBytes(right.AccessToken));
    }

    private static JsonObject? TryReadObject(string path)
    {
        if (!File.Exists(path)) return null;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return JsonNode.Parse(stream) as JsonObject;
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(80 * attempt);
            }
            catch (JsonException)
            {
                // Claude Code가 파일을 쓰는 도중일 수 있으므로 잠시 후 다시 읽습니다.
                if (attempt == 3) return null;
                Thread.Sleep(80 * attempt);
            }
        }

        return null;
    }

    private static string? GetString(JsonObject node, string property) =>
        node[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    internal static void WriteAtomically(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".widget-tmp";

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.WriteAllText(temporary, content, new UTF8Encoding(false));
                File.Move(temporary, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(120 * attempt);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(120 * attempt);
            }
        }
    }
}
