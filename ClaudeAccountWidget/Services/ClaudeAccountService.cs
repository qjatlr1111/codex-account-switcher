using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.Json.Nodes;
using ClaudeAccountWidget.Models;

namespace ClaudeAccountWidget.Services;

public sealed class ClaudeAccountService(ClaudeApiClient api, ClaudeProcessMonitor processes)
{
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(10);

    private readonly object _accountCacheLock = new();
    private (string Path, DateTime WriteTime, long Length, ClaudeOAuthAccount? Account)? _accountCache;

    public string? CurrentAccountUuid() => CurrentAccount()?.AccountUuid;

    /// <summary>
    /// ~/.claude.json은 프로젝트 기록 때문에 수 MB까지 커질 수 있으므로,
    /// 수정 시각과 크기가 바뀌었을 때만 다시 파싱합니다.
    /// </summary>
    public ClaudeOAuthAccount? CurrentAccount()
    {
        var path = ClaudePaths.MainGlobalConfigPath;
        var info = new FileInfo(path);
        if (!info.Exists) return null;

        lock (_accountCacheLock)
        {
            if (_accountCache is { } cache && cache.Path == path &&
                cache.WriteTime == info.LastWriteTimeUtc && cache.Length == info.Length)
                return cache.Account;

            var account = ClaudeCredentialStore.TryReadOAuthAccount(path);
            _accountCache = (path, info.LastWriteTimeUtc, info.Length, account);
            return account;
        }
    }

    public bool HasCurrentLogin() =>
        ClaudeCredentialStore.TryReadCredentials(ClaudePaths.MainCredentialsPath) is not null;

    /// <summary>
    /// 계정 사용량을 새로 읽습니다. 활성 계정은 Claude Code가 계속 갱신하는 기본 인증 파일을,
    /// 비활성 계정은 위젯 프로필의 인증 파일을 사용합니다.
    /// </summary>
    public async Task RefreshAsync(AccountProfile profile, CancellationToken cancellationToken = default)
    {
        profile.IsBusy = true;
        profile.Status = "사용량 확인 중";

        try
        {
            var credentialsPath = profile.IsActive
                ? ClaudePaths.MainCredentialsPath
                : ClaudePaths.ProfileCredentialsPath(profile.HomePath);

            var credentials = ClaudeCredentialStore.TryReadCredentials(credentialsPath);
            if (credentials is null)
            {
                profile.Status = "로그인 정보 없음 · 계정을 다시 추가해 주세요";
                profile.PrimaryRemaining = null;
                profile.SecondaryRemaining = null;
                return;
            }

            ApplyPlan(profile, credentials);

            if (credentials.IsExpired(ExpiryMargin))
            {
                credentials = await TryRenewAsync(profile, credentialsPath, credentials, cancellationToken);
                if (credentials is null) return;
            }

            UsageSnapshot usage;
            try
            {
                usage = await api.GetUsageAsync(credentials.AccessToken, cancellationToken);
            }
            catch (ClaudeApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized && profile.IsActive)
            {
                // 실행 중인 Claude Code가 방금 토큰을 교체했을 수 있으므로 한 번만 다시 읽습니다.
                var reread = ClaudeCredentialStore.TryReadCredentials(credentialsPath);
                if (reread is null || reread.AccessToken == credentials.AccessToken) throw;
                credentials = reread;
                usage = await api.GetUsageAsync(credentials.AccessToken, cancellationToken);
            }

            ApplyUsage(profile, usage);

            if (string.IsNullOrEmpty(profile.AccountUuid) || profile.Email.Contains('@') == false)
                await FillIdentityAsync(profile, credentials.AccessToken, cancellationToken);

            profile.Status = $"최신 정보 · {DateTime.Now:HH:mm}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ClaudeApiException exception) when (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // 조회 한도 초과 시 기존 수치를 유지합니다.
            profile.Status = exception.Message;
        }
        catch (Exception exception)
        {
            profile.Status = FriendlyError(exception);
        }
        finally
        {
            profile.IsBusy = false;
        }
    }

    private async Task<ClaudeCredentials?> TryRenewAsync(
        AccountProfile profile,
        string credentialsPath,
        ClaudeCredentials credentials,
        CancellationToken cancellationToken)
    {
        // 활성 계정의 토큰을 Claude Code가 실행 중일 때 갱신하면 refresh token 교체 경쟁이 생겨
        // Claude Code 세션이 로그아웃될 수 있습니다. 이 경우 Claude Code가 스스로 갱신하도록 둡니다.
        if (profile.IsActive && processes.FindCliSessions().Count > 0)
        {
            profile.Status = "토큰 갱신 대기 · Claude Code가 곧 갱신합니다";
            return null;
        }

        if (string.IsNullOrWhiteSpace(credentials.RefreshToken))
        {
            profile.Status = "로그인 만료 · 계정을 다시 추가해 주세요";
            return null;
        }

        try
        {
            profile.Status = "토큰 갱신 중";
            var renewed = await api.RefreshAsync(credentials.RefreshToken, cancellationToken);
            ClaudeCredentialStore.UpdateTokens(credentialsPath, renewed.AccessToken, renewed.RefreshToken, renewed.ExpiresAt);
            return ClaudeCredentialStore.TryReadCredentials(credentialsPath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            profile.Status = profile.IsActive
                ? "토큰 만료 · Claude Code를 한 번 실행하면 갱신됩니다"
                : "토큰 만료 · 전환하면 Claude Code가 갱신합니다";
            return null;
        }
    }

    /// <summary>
    /// 새 계정 로그인을 시작합니다. 위젯 전용 CLAUDE_CONFIG_DIR로 Claude Code의 공식 로그인 창을 열고,
    /// 인증 파일이 만들어지면 창을 닫습니다.
    /// </summary>
    public async Task<bool> LoginAsync(AccountProfile profile, CancellationToken cancellationToken = default)
    {
        profile.IsBusy = true;
        profile.Status = "로그인 창을 여는 중";
        Process? console = null;

        try
        {
            var cli = ClaudeProcessMonitor.FindClaudeCli()
                      ?? throw new FileNotFoundException(
                          "Claude Code를 찾지 못했습니다. claude.ai/code 안내에 따라 먼저 설치해 주세요.");
            Directory.CreateDirectory(profile.HomePath);

            var supportsAuthLogin = await SupportsAuthLoginAsync(cli, profile.HomePath, cancellationToken);
            console = StartLoginConsole(cli, profile.HomePath, supportsAuthLogin);
            profile.Status = "브라우저 로그인 대기 중";

            var credentialsPath = ClaudePaths.ProfileCredentialsPath(profile.HomePath);
            var deadline = DateTime.UtcNow + LoginTimeout;
            DateTime? exitedAt = null;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ClaudeCredentialStore.TryReadCredentials(credentialsPath) is not null)
                {
                    // 계정 정보(.claude.json)가 이어서 기록될 시간을 잠시 줍니다.
                    var accountDeadline = DateTime.UtcNow.AddSeconds(6);
                    while (DateTime.UtcNow < accountDeadline &&
                           ClaudeCredentialStore.TryReadOAuthAccount(
                               ClaudePaths.ProfileGlobalConfigPath(profile.HomePath)) is null)
                        await Task.Delay(300, cancellationToken);

                    await CompleteIdentityAfterLoginAsync(profile, cancellationToken);
                    profile.Status = "로그인 완료";
                    return true;
                }

                if (console.HasExited)
                {
                    exitedAt ??= DateTime.UtcNow;
                    if (DateTime.UtcNow - exitedAt > TimeSpan.FromSeconds(2))
                    {
                        profile.Status = "로그인 창이 닫혔습니다";
                        return false;
                    }
                }

                await Task.Delay(700, cancellationToken);
            }

            profile.Status = "로그인 시간 초과";
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            profile.Status = "로그인 취소됨";
            return false;
        }
        catch (Exception exception)
        {
            profile.Status = FriendlyError(exception);
            return false;
        }
        finally
        {
            if (console is not null)
            {
                try { if (!console.HasExited) console.Kill(entireProcessTree: true); }
                catch { }
                console.Dispose();
            }

            profile.IsBusy = false;
        }
    }

    /// <summary>지금 Claude Code에 로그인된 계정을 다시 로그인하지 않고 위젯에 등록합니다.</summary>
    public async Task ImportCurrentAsync(AccountProfile profile, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(profile.HomePath);
        ClaudeCredentialStore.CopyCredentials(
            ClaudePaths.MainCredentialsPath,
            ClaudePaths.ProfileCredentialsPath(profile.HomePath));

        var account = CurrentAccount();
        if (account is not null)
        {
            ClaudeCredentialStore.WriteOAuthAccount(ClaudePaths.ProfileGlobalConfigPath(profile.HomePath), account.Raw);
            ApplyAccount(profile, account);
        }

        await CompleteIdentityAfterLoginAsync(profile, cancellationToken);
    }

    /// <summary>
    /// 활성 계정은 사용 중에 Claude Code가 토큰을 교체합니다. 다른 계정으로 바꾸기 전에
    /// 최신 인증을 위젯 프로필로 되돌려 저장해야 나중에 다시 전환할 수 있습니다.
    /// </summary>
    public void SaveActiveLoginBack(AccountProfile activeProfile)
    {
        var current = CurrentAccount();
        if (current?.AccountUuid is null ||
            !string.Equals(current.AccountUuid, activeProfile.AccountUuid, StringComparison.OrdinalIgnoreCase))
            return;
        if (ClaudeCredentialStore.TryReadCredentials(ClaudePaths.MainCredentialsPath) is null) return;

        ClaudeCredentialStore.CopyCredentials(
            ClaudePaths.MainCredentialsPath,
            ClaudePaths.ProfileCredentialsPath(activeProfile.HomePath));
        ClaudeCredentialStore.WriteOAuthAccount(
            ClaudePaths.ProfileGlobalConfigPath(activeProfile.HomePath), current.Raw);
    }

    public void Activate(AccountProfile profile)
    {
        var source = ClaudePaths.ProfileCredentialsPath(profile.HomePath);
        if (ClaudeCredentialStore.TryReadCredentials(source) is null)
            throw new InvalidOperationException("선택한 계정의 로그인 정보가 없습니다. 계정을 다시 추가해 주세요.");

        var backup = Path.Combine(ClaudePaths.MainConfigDir, ".credentials.widget-backup.json");
        ClaudeCredentialStore.CopyCredentials(source, ClaudePaths.MainCredentialsPath, backup);

        var account = ClaudeCredentialStore.TryReadOAuthAccount(ClaudePaths.ProfileGlobalConfigPath(profile.HomePath));
        if (account is not null)
            ClaudeCredentialStore.WriteOAuthAccount(ClaudePaths.MainGlobalConfigPath, account.Raw);
    }

    public bool IsActivated(AccountProfile profile) =>
        ClaudeCredentialStore.SameAccessToken(
            ClaudePaths.ProfileCredentialsPath(profile.HomePath),
            ClaudePaths.MainCredentialsPath);

    private async Task CompleteIdentityAfterLoginAsync(AccountProfile profile, CancellationToken cancellationToken)
    {
        var configPath = ClaudePaths.ProfileGlobalConfigPath(profile.HomePath);
        var account = ClaudeCredentialStore.TryReadOAuthAccount(configPath);
        if (account is not null) ApplyAccount(profile, account);

        var credentials = ClaudeCredentialStore.TryReadCredentials(ClaudePaths.ProfileCredentialsPath(profile.HomePath));
        if (credentials is null) return;
        ApplyPlan(profile, credentials);

        if (account is null || string.IsNullOrEmpty(profile.AccountUuid))
        {
            var info = await FillIdentityAsync(profile, credentials.AccessToken, cancellationToken);
            if (account is null && info is not null)
            {
                // 전환 시 Claude Code가 계정을 바로 알아보도록 최소한의 oauthAccount를 만들어 둡니다.
                ClaudeCredentialStore.WriteOAuthAccount(configPath, new JsonObject
                {
                    ["accountUuid"] = info.AccountUuid,
                    ["emailAddress"] = info.Email,
                    ["displayName"] = info.DisplayName,
                    ["organizationName"] = info.OrganizationName
                });
            }
        }
    }

    private async Task<ClaudeProfileInfo?> FillIdentityAsync(
        AccountProfile profile, string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            var info = await api.GetProfileAsync(accessToken, cancellationToken);
            if (!string.IsNullOrWhiteSpace(info.Email)) profile.Email = info.Email;
            if (!string.IsNullOrWhiteSpace(info.AccountUuid)) profile.AccountUuid = info.AccountUuid;
            if (!string.IsNullOrWhiteSpace(info.OrganizationName)) profile.OrganizationName = info.OrganizationName;
            return info;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyAccount(AccountProfile profile, ClaudeOAuthAccount account)
    {
        if (!string.IsNullOrWhiteSpace(account.Email)) profile.Email = account.Email;
        if (!string.IsNullOrWhiteSpace(account.AccountUuid)) profile.AccountUuid = account.AccountUuid;
        if (!string.IsNullOrWhiteSpace(account.OrganizationName)) profile.OrganizationName = account.OrganizationName;
    }

    private static void ApplyPlan(AccountProfile profile, ClaudeCredentials credentials)
    {
        var plan = credentials.SubscriptionType;
        if (string.IsNullOrWhiteSpace(plan)) return;

        var tier = credentials.RateLimitTier ?? string.Empty;
        var multiplier = tier.Contains("20x", StringComparison.OrdinalIgnoreCase) ? " 20x"
            : tier.Contains("5x", StringComparison.OrdinalIgnoreCase) ? " 5x"
            : string.Empty;
        profile.PlanType = plan + multiplier;
    }

    private static void ApplyUsage(AccountProfile profile, UsageSnapshot usage)
    {
        profile.PrimaryRemaining = usage.FiveHour?.Remaining;
        profile.PrimaryResetsAt = usage.FiveHour?.ResetsAt;
        profile.SecondaryRemaining = usage.SevenDay?.Remaining;
        profile.SecondaryResetsAt = usage.SevenDay?.ResetsAt;

        var extras = new List<string>();
        if (usage.SevenDayOpus?.Remaining is { } opus) extras.Add($"Opus 주간 {opus}%");
        if (usage.SevenDaySonnet?.Remaining is { } sonnet) extras.Add($"Sonnet 주간 {sonnet}%");
        profile.ExtraUsageText = string.Join("  ·  ", extras);
    }

    private static async Task<bool> SupportsAuthLoginAsync(string cli, string configDir, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = CreateCliStartInfo(cli, configDir, "auth --help");
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;

            using var process = Process.Start(startInfo);
            if (process is null) return false;
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            _ = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(12), cancellationToken);
            }
            catch (TimeoutException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            var output = await outputTask;
            return process.ExitCode == 0 && output.Contains("login", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static Process StartLoginConsole(string cli, string configDir, bool supportsAuthLogin)
    {
        var command = supportsAuthLogin ? "auth login" : string.Empty;
        var guide = supportsAuthLogin
            ? "로그인이 끝나면 이 창은 자동으로 닫힙니다."
            : "로그인 후 안내가 나오면 이 창은 위젯이 자동으로 닫습니다.";
        var script =
            "chcp 65001>nul & title Claude 계정 추가 & echo. " +
            "& echo   [Claude Account Switcher] 브라우저에서 추가할 Claude 계정으로 로그인하세요. " +
            $"& echo   {guide} & echo. & \"{cli}\" {command}";

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            Arguments = $"/d /s /k \"{script}\"",
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = configDir
        };
        PrepareEnvironment(startInfo, configDir);

        return Process.Start(startInfo)
               ?? throw new InvalidOperationException("로그인 창을 열지 못했습니다.");
    }

    private static ProcessStartInfo CreateCliStartInfo(string cli, string configDir, string arguments)
    {
        var startInfo = new ProcessStartInfo { UseShellExecute = false, WorkingDirectory = configDir };
        if (Path.GetExtension(cli).Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            startInfo.Arguments = $"/d /s /c \"\"{cli}\" {arguments}\"";
        }
        else
        {
            startInfo.FileName = cli;
            startInfo.Arguments = arguments;
        }

        PrepareEnvironment(startInfo, configDir);
        return startInfo;
    }

    private static void PrepareEnvironment(ProcessStartInfo startInfo, string configDir)
    {
        startInfo.Environment["CLAUDE_CONFIG_DIR"] = configDir;
        // 환경 변수 인증이 있으면 Claude Code가 브라우저 로그인을 건너뛰므로 로그인 창에서만 제거합니다.
        startInfo.Environment.Remove("ANTHROPIC_API_KEY");
        startInfo.Environment.Remove("ANTHROPIC_AUTH_TOKEN");
        startInfo.Environment.Remove("CLAUDE_CODE_OAUTH_TOKEN");
    }

    private static string FriendlyError(Exception exception)
    {
        if (exception is ClaudeApiException api) return api.Message;
        if (exception is TimeoutException or TaskCanceledException) return "Claude 응답 시간 초과";
        if (exception is System.Net.Http.HttpRequestException) return "네트워크 연결 실패";
        return exception.Message.Length > 60 ? exception.Message[..60] + "…" : exception.Message;
    }
}
