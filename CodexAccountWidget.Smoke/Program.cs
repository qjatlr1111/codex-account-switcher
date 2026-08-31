using CodexAccountWidget.Services;

if (args.Length == 1 && args[0] == "--detect-codex")
{
    var detector = new CodexDesktopRestartService();
    var processIds = detector.FindRunningProcessIds();
    var hasWindow = detector.HasRunningWindow();
    Console.WriteLine($"PASS: OpenAI.Codex 패키지 프로세스 {processIds.Count}개, 표시 창 {hasWindow} 탐지");
    return 0;
}

if (args.Length != 1)
{
    Console.Error.WriteLine("사용법: CodexAccountWidget.Smoke <임시 CODEX_HOME> | --detect-codex");
    return 2;
}

try
{
    await using var client = new CodexAppServerClient(Path.GetFullPath(args[0]));
    await client.StartAsync();
    var account = await client.SendRequestAsync("account/read", new { refreshToken = false });

    if (!account.TryGetProperty("requiresOpenaiAuth", out _))
        throw new InvalidOperationException("account/read 응답에 requiresOpenaiAuth가 없습니다.");

    var loginCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    client.NotificationReceived += (_, notification) =>
    {
        if (notification.Method != "account/login/completed") return;
        var success = notification.Parameters.TryGetProperty("success", out var value) && value.GetBoolean();
        loginCompleted.TrySetResult(success);
    };

    var login = await client.SendRequestAsync("account/login/start", new
    {
        type = "chatgpt",
        useHostedLoginSuccessPage = true,
        appBrand = "codex"
    });
    var loginId = login.GetProperty("loginId").GetString()
                  ?? throw new InvalidOperationException("loginId가 없습니다.");
    await client.SendRequestAsync("account/login/cancel", new { loginId });
    if (await loginCompleted.Task.WaitAsync(TimeSpan.FromSeconds(10)))
        throw new InvalidOperationException("취소된 로그인의 완료 이벤트가 성공으로 표시됐습니다.");

    Console.WriteLine("PASS: initialize, account/read 및 로그인 취소 응답 확인");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"FAIL: {exception.Message}");
    return 1;
}
