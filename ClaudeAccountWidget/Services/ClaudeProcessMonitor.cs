using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeAccountWidget.Services;

public enum ClaudeProcessKind
{
    /// <summary>Claude 데스크톱 앱(Electron).</summary>
    Desktop,
    /// <summary>데스크톱 앱이 관리하는 내장 Claude Code.</summary>
    DesktopManagedCode,
    /// <summary>터미널에서 실행한 Claude Code CLI.</summary>
    CodeCli
}

public sealed record ClaudeProcessInfo(int Id, ClaudeProcessKind Kind, string ImagePath);

/// <summary>
/// 실행 중인 Claude 데스크톱 앱과 Claude Code CLI를 찾습니다.
/// 두 프로그램 모두 프로세스 이름이 claude이므로 실행 파일 경로로 구분합니다.
/// </summary>
public sealed class ClaudeProcessMonitor
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public IReadOnlyList<ClaudeProcessInfo> FindClaudeProcesses()
    {
        var result = new List<ClaudeProcessInfo>();
        foreach (var process in Process.GetProcessesByName("claude"))
        {
            try
            {
                var path = TryGetImagePath(process.Id) ?? string.Empty;
                result.Add(new ClaudeProcessInfo(process.Id, Classify(path), path));
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }

    public IReadOnlyList<ClaudeProcessInfo> FindCliSessions() =>
        FindClaudeProcesses().Where(process => process.Kind == ClaudeProcessKind.CodeCli).ToList();

    /// <summary>Claude 데스크톱 창이 보이거나 Claude Code CLI가 실행 중이면 true입니다.</summary>
    public bool IsClaudeRunning()
    {
        var processes = FindClaudeProcesses();
        if (processes.Count == 0) return false;
        if (processes.Any(process => process.Kind == ClaudeProcessKind.CodeCli)) return true;

        var desktopIds = processes
            .Where(process => process.Kind == ClaudeProcessKind.Desktop)
            .Select(process => process.Id)
            .ToHashSet();
        if (desktopIds.Count == 0) return false;

        var found = false;
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var processId);
            if (desktopIds.Contains(unchecked((int)processId)) && IsWindowVisible(handle) &&
                GetWindowTextLength(handle) > 0)
            {
                found = true;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>
    /// 터미널의 Claude Code 세션만 종료합니다. 데스크톱 앱은 건드리지 않습니다.
    /// 종료된 세션은 해당 폴더에서 `claude --continue`로 이어갈 수 있습니다.
    /// </summary>
    public async Task<int> StopCliSessionsAsync(CancellationToken cancellationToken = default)
    {
        var sessions = FindCliSessions();
        var stopped = new List<Process>();

        foreach (var session in sessions)
        {
            try
            {
                var process = Process.GetProcessById(session.Id);
                process.Kill(entireProcessTree: true);
                stopped.Add(process);
            }
            catch (ArgumentException)
            {
                // 이미 종료된 프로세스입니다.
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception exception)
            {
                throw new InvalidOperationException(
                    $"Claude Code 세션({session.Id})을 종료하지 못했습니다: {exception.Message}", exception);
            }
        }

        foreach (var process in stopped)
        {
            try
            {
                await process.WaitForExitAsync(cancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (TimeoutException) { }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }

        // 세션이 종료 직전 인증 파일을 쓸 수 있으므로 잠시 기다립니다.
        if (stopped.Count > 0) await Task.Delay(600, cancellationToken);
        return stopped.Count;
    }

    private static ClaudeProcessKind Classify(string imagePath)
    {
        var path = imagePath.Replace('/', '\\');
        if (path.Contains(@"\AnthropicClaude\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"\WindowsApps\Claude", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"\WindowsApps\Anthropic", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"\Programs\Claude\", StringComparison.OrdinalIgnoreCase))
            return ClaudeProcessKind.Desktop;

        if (path.Contains(@"\Claude\claude-code", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"\Claude\claude-code-vm", StringComparison.OrdinalIgnoreCase))
            return ClaudeProcessKind.DesktopManagedCode;

        // 경로를 읽지 못한 경우: 창이 있는 claude는 데스크톱 앱일 가능성이 높으므로 보수적으로 판단합니다.
        if (path.Length == 0) return ClaudeProcessKind.Desktop;
        return ClaudeProcessKind.CodeCli;
    }

    private static string? TryGetImagePath(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return null;

        try
        {
            var capacity = 1024;
            var buffer = new StringBuilder(capacity);
            return QueryFullProcessImageName(handle, 0, buffer, ref capacity)
                ? buffer.ToString(0, capacity)
                : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public static string? FindClaudeCli()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var nativeInstall = Path.Combine(home, ".local", "bin", "claude.exe");
        if (File.Exists(nativeInstall)) return nativeInstall;

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var directory in pathEntries)
        {
            foreach (var fileName in new[] { "claude.exe", "claude.cmd" })
            {
                try
                {
                    var candidate = Path.Combine(directory, fileName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
            }
        }

        var npmCommand = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "claude.cmd");
        return File.Exists(npmCommand) ? npmCommand : null;
    }

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder exeName, ref int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr windowHandle);
}
