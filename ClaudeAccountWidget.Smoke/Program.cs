using ClaudeAccountWidget.Services;

// 실제 토큰이나 이메일 전체는 출력하지 않는 읽기 전용 점검 도구입니다.
var processes = new ClaudeProcessMonitor();

if (args.Length == 1 && args[0] == "--detect-claude")
{
    var found = processes.FindClaudeProcesses();
    foreach (var group in found.GroupBy(process => process.Kind))
        Console.WriteLine($"  {group.Key}: {group.Count()}개");
    Console.WriteLine($"PASS: claude 프로세스 {found.Count}개, 위젯 표시 조건 {processes.IsClaudeRunning()}");
    Console.WriteLine($"  Claude Code CLI 경로: {ClaudeProcessMonitor.FindClaudeCli() ?? "(찾지 못함)"}");
    return 0;
}

if (args.Length == 1 && args[0] == "--check-current")
{
    var credentials = ClaudeCredentialStore.TryReadCredentials(ClaudePaths.MainCredentialsPath);
    if (credentials is null)
    {
        Console.WriteLine($"FAIL: 로그인 정보 없음 ({ClaudePaths.MainCredentialsPath})");
        return 1;
    }

    var account = ClaudeCredentialStore.TryReadOAuthAccount(ClaudePaths.MainGlobalConfigPath);
    Console.WriteLine($"  계정: {Mask(account?.Email)} / 요금제: {credentials.SubscriptionType ?? "?"}" +
                      $" / 만료: {credentials.ExpiresAt?.ToLocalTime():yyyy-MM-dd HH:mm}");
    if (credentials.IsExpired(TimeSpan.Zero))
    {
        Console.WriteLine("SKIP: 토큰이 만료되어 사용량 조회를 건너뜁니다. Claude Code를 한 번 실행하세요.");
        return 0;
    }

    var usage = await new ClaudeApiClient().GetUsageAsync(credentials.AccessToken, CancellationToken.None);
    Console.WriteLine($"PASS: 5시간 잔여 {usage.FiveHour?.Remaining?.ToString() ?? "--"}%," +
                      $" 주간 잔여 {usage.SevenDay?.Remaining?.ToString() ?? "--"}%");
    return 0;
}

if (args.Length == 1 && args[0] == "--check-updates")
{
    var result = await new UpdateCheckService().CheckAsync();
    if (result.InstallerUri.Host != "github.com" || result.ChecksumUri.Host != "github.com")
        throw new InvalidOperationException("업데이트 파일 주소가 안전하지 않습니다.");
    Console.WriteLine($"PASS: 업데이트 확인 (현재 {result.CurrentVersion}, 최신 {result.LatestTagName})");
    return 0;
}

Console.WriteLine("사용법: --detect-claude | --check-current | --check-updates");
return 2;

static string Mask(string? email)
{
    if (string.IsNullOrEmpty(email)) return "(알 수 없음)";
    var at = email.IndexOf('@');
    return at <= 1 ? "***" + email[Math.Max(0, at)..] : email[0] + "***" + email[at..];
}
