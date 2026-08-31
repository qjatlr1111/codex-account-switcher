using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace CodexAccountWidget.Services;

public sealed class CodexAppServerClient : IAsyncDisposable
{
    private readonly string _codexHome;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _process;
    private StreamWriter? _input;
    private Task? _readerTask;
    private long _nextId;

    public event EventHandler<AppServerNotification>? NotificationReceived;

    public CodexAppServerClient(string codexHome) => _codexHome = codexHome;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_codexHome);

        var startInfo = CreateAppServerStartInfo();
        startInfo.Environment["CODEX_HOME"] = _codexHome;

        _process = Process.Start(startInfo)
                   ?? throw new InvalidOperationException("Codex App Server를 시작하지 못했습니다.");
        _input = _process.StandardInput;
        _input.AutoFlush = true;
        _readerTask = ReadLoopAsync(_process.StandardOutput, _lifetime.Token);
        _ = DrainErrorsAsync(_process.StandardError, _lifetime.Token);

        await SendRequestAsync("initialize", new
        {
            clientInfo = new
            {
                name = "codex_account_widget",
                title = "Codex Account Widget",
                version = "0.1.0"
            }
        }, cancellationToken);

        await SendNotificationAsync("initialized", new { });
    }

    private static ProcessStartInfo CreateAppServerStartInfo()
    {
        var command = FindCodexCommand();
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (Path.GetExtension(command).Equals(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(command);
        }

        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--stdio");
        return startInfo;
    }

    private static string FindCodexCommand()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var bundledBinRoot = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
        if (Directory.Exists(bundledBinRoot))
        {
            var bundledExecutable = Directory.GetDirectories(bundledBinRoot)
                .Select(directory => Path.Combine(directory, "codex.exe"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (bundledExecutable is not null) return bundledExecutable;
        }

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var directory in pathEntries)
        {
            foreach (var fileName in new[] { "codex.exe", "codex.cmd" })
            {
                var candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate)) return candidate;
            }
        }

        var npmCommand = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm",
            "codex.cmd");
        if (File.Exists(npmCommand)) return npmCommand;

        throw new FileNotFoundException(
            "Codex CLI를 찾지 못했습니다. Codex 데스크톱 앱 또는 전역 codex 명령을 설치해 주세요.");
    }

    public async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        try
        {
            await WriteAsync(new { method, id, @params = parameters });
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(35), cancellationToken);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private Task SendNotificationAsync(string method, object? parameters) =>
        WriteAsync(new { method, @params = parameters });

    private async Task WriteAsync(object message)
    {
        if (_input is null) throw new InvalidOperationException("App Server가 시작되지 않았습니다.");
        var json = JsonSerializer.Serialize(message);
        await _input.WriteLineAsync(json);
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id))
            {
                if (!_pending.TryGetValue(id, out var completion)) continue;

                if (root.TryGetProperty("error", out var error))
                {
                    var message = error.TryGetProperty("message", out var detail)
                        ? detail.GetString()
                        : error.GetRawText();
                    completion.TrySetException(new InvalidOperationException(message));
                }
                else if (root.TryGetProperty("result", out var result))
                {
                    completion.TrySetResult(result.Clone());
                }

                continue;
            }

            if (root.TryGetProperty("method", out var methodElement))
            {
                var method = methodElement.GetString() ?? string.Empty;
                var parameters = root.TryGetProperty("params", out var value)
                    ? value.Clone()
                    : default;
                NotificationReceived?.Invoke(this, new AppServerNotification(method, parameters));
            }
        }

        foreach (var completion in _pending.Values)
            completion.TrySetException(new IOException("Codex App Server 연결이 종료되었습니다."));
    }

    private static async Task DrainErrorsAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested &&
               await reader.ReadLineAsync(cancellationToken) is not null)
        {
            // 인증 정보가 포함될 가능성을 피하기 위해 stderr를 로그로 남기지 않습니다.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();

        try { _input?.Close(); } catch { }

        if (_process is { HasExited: false })
        {
            try { _process.Kill(true); } catch { }
        }

        if (_readerTask is not null)
        {
            try { await _readerTask; } catch { }
        }

        _process?.Dispose();
        _lifetime.Dispose();
    }
}

public sealed record AppServerNotification(string Method, JsonElement Parameters);
