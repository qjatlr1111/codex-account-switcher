using System.Diagnostics;
using System.IO;
using System.Text.Json;
using CodexAccountWidget.Models;

namespace CodexAccountWidget.Services;

public sealed class CodexAccountService
{
    public async Task RefreshAsync(AccountProfile profile, CancellationToken cancellationToken = default)
    {
        profile.IsBusy = true;
        profile.Status = "사용량 확인 중";

        try
        {
            await using var client = new CodexAppServerClient(profile.HomePath);
            await client.StartAsync(cancellationToken);

            var account = await client.SendRequestAsync(
                "account/read",
                new { refreshToken = false },
                cancellationToken);

            if (!account.TryGetProperty("account", out var accountData) || accountData.ValueKind == JsonValueKind.Null)
            {
                profile.Email = "로그인 필요";
                profile.Status = "계정을 다시 추가해 주세요";
                profile.PrimaryRemaining = null;
                profile.SecondaryRemaining = null;
                return;
            }

            profile.Email = accountData.TryGetProperty("email", out var email)
                ? email.GetString() ?? "ChatGPT 계정"
                : "API 계정";
            profile.DisplayName = profile.Email;
            profile.PlanType = accountData.TryGetProperty("planType", out var plan)
                ? plan.GetString() ?? "unknown"
                : "unknown";

            var limits = await client.SendRequestAsync(
                "account/rateLimits/read",
                null,
                cancellationToken);

            ApplyRateLimits(profile, limits);
            profile.Status = "최신 정보";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            profile.Status = FriendlyError(exception);
            profile.PrimaryRemaining = null;
            profile.SecondaryRemaining = null;
        }
        finally
        {
            profile.IsBusy = false;
        }
    }

    public async Task<bool> LoginAsync(AccountProfile profile, CancellationToken cancellationToken = default)
    {
        profile.IsBusy = true;
        profile.Status = "브라우저 로그인 대기 중";

        try
        {
            await using var client = new CodexAppServerClient(profile.HomePath);
            var loginCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            client.NotificationReceived += (_, notification) =>
            {
                if (notification.Method != "account/login/completed") return;
                var success = notification.Parameters.TryGetProperty("success", out var value) && value.GetBoolean();
                loginCompleted.TrySetResult(success);
            };

            await client.StartAsync(cancellationToken);
            var login = await client.SendRequestAsync(
                "account/login/start",
                new
                {
                    type = "chatgpt",
                    useHostedLoginSuccessPage = true,
                    appBrand = "codex"
                },
                cancellationToken);

            var authUrl = login.GetProperty("authUrl").GetString()
                          ?? throw new InvalidOperationException("로그인 주소를 받지 못했습니다.");

            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
            var success = await loginCompleted.Task.WaitAsync(TimeSpan.FromMinutes(5), cancellationToken);
            if (!success)
            {
                profile.Status = "로그인 실패";
                return false;
            }

            profile.Status = "로그인 완료";
            return true;
        }
        catch (Exception exception)
        {
            profile.Status = FriendlyError(exception);
            return false;
        }
        finally
        {
            profile.IsBusy = false;
        }
    }

    public async Task ActivateForNextCodexLaunchAsync(AccountProfile profile)
    {
        var source = Path.Combine(profile.HomePath, "auth.json");
        if (!File.Exists(source))
            throw new InvalidOperationException("선택한 계정의 인증 캐시가 없습니다.");

        var defaultHome = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex");
        Directory.CreateDirectory(defaultHome);

        var target = Path.Combine(defaultHome, "auth.json");
        var backup = Path.Combine(defaultHome, "auth.widget-backup.json");
        var temporary = target + ".widget-tmp";

        if (File.Exists(target)) File.Copy(target, backup, true);
        File.Copy(source, temporary, true);
        File.Move(temporary, target, true);

        await Task.CompletedTask;
    }

    private static void ApplyRateLimits(AccountProfile profile, JsonElement response)
    {
        var snapshot = response.GetProperty("rateLimits");
        ApplyWindow(profile, snapshot, "primary", true);
        ApplyWindow(profile, snapshot, "secondary", false);
    }

    private static void ApplyWindow(AccountProfile profile, JsonElement snapshot, string property, bool primary)
    {
        if (!snapshot.TryGetProperty(property, out var window) || window.ValueKind == JsonValueKind.Null)
        {
            if (primary) profile.PrimaryRemaining = null;
            else profile.SecondaryRemaining = null;
            return;
        }

        var used = window.GetProperty("usedPercent").GetInt32();
        var remaining = Math.Clamp(100 - used, 0, 100);
        var minutes = window.TryGetProperty("windowDurationMins", out var duration) && duration.ValueKind != JsonValueKind.Null
            ? duration.GetInt64()
            : 0;
        var label = FormatWindowLabel(minutes, primary);

        if (primary)
        {
            profile.PrimaryRemaining = remaining;
            profile.PrimaryLabel = label;
        }
        else
        {
            profile.SecondaryRemaining = remaining;
            profile.SecondaryLabel = label;
        }
    }

    private static string FormatWindowLabel(long minutes, bool primary)
    {
        if (minutes >= 6 * 24 * 60) return "주간";
        if (minutes >= 60 && minutes % 60 == 0) return $"{minutes / 60}시간";
        if (minutes > 0) return $"{minutes}분";
        return primary ? "단기" : "장기";
    }

    private static string FriendlyError(Exception exception)
    {
        if (exception.Message.Contains("not logged", StringComparison.OrdinalIgnoreCase)) return "로그인 필요";
        if (exception is TimeoutException) return "Codex 응답 시간 초과";
        return exception.Message.Length > 54 ? exception.Message[..54] + "…" : exception.Message;
    }
}
