using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexAccountWidget.Services;

public sealed class CodexDesktopRestartService
{
    private const string DefaultApplicationUserModelId = "OpenAI.Codex_2p2nqsd0c76g0!App";
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint WmClose = 0x0010;

    public async Task<CodexLaunchTarget> StopAsync(CancellationToken cancellationToken = default)
    {
        var processes = FindCodexDesktopProcesses();
        var applicationUserModelId = FindApplicationUserModelId(processes)
                                     ?? DefaultApplicationUserModelId;

        if (processes.Count == 0)
            return new CodexLaunchTarget(applicationUserModelId, WasRunning: false);

        var processIds = processes.Select(process => process.Id).ToHashSet();
        RequestGracefulClose(processIds);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && processes.Any(IsStillRunning))
        {
            await Task.Delay(200, cancellationToken);
        }

        foreach (var process in processes.Where(IsStillRunning))
        {
            try
            {
                // 위젯이 Codex에서 실행된 경우 자식 프로세스로 연결될 수 있으므로
                // 전체 트리를 종료하지 않고 패키지의 ChatGPT 프로세스만 개별 종료합니다.
                process.Kill(entireProcessTree: false);
            }
            catch (InvalidOperationException)
            {
                // 종료 경쟁으로 이미 사라진 프로세스입니다.
            }
            catch (Win32Exception exception)
            {
                throw new InvalidOperationException(
                    $"Codex 프로세스({process.Id})를 종료하지 못했습니다: {exception.Message}", exception);
            }
        }

        foreach (var process in processes)
        {
            try { await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken); }
            catch (InvalidOperationException) { }
            finally { process.Dispose(); }
        }

        return new CodexLaunchTarget(applicationUserModelId, WasRunning: true);
    }

    public async Task StartAsync(CodexLaunchTarget target, CancellationToken cancellationToken = default)
    {
        await Task.Delay(650, cancellationToken);

        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"shell:AppsFolder\\{target.ApplicationUserModelId}",
            UseShellExecute = true
        };

        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Codex 데스크톱 앱을 다시 시작하지 못했습니다.");

        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FindProcessIdsAndDispose().Length > 0) return;
            await Task.Delay(250, cancellationToken);
        }

        throw new InvalidOperationException("Codex 재실행 요청 후 앱 프로세스가 확인되지 않았습니다.");
    }

    public IReadOnlyList<int> FindRunningProcessIds() =>
        FindProcessIdsAndDispose();

    public bool HasRunningWindow()
    {
        var processIds = FindProcessIdsAndDispose().ToHashSet();
        if (processIds.Count == 0) return false;

        var found = false;
        EnumWindows((windowHandle, _) =>
        {
            GetWindowThreadProcessId(windowHandle, out var processId);
            if (processIds.Contains(unchecked((int)processId)) && IsWindowVisible(windowHandle))
            {
                found = true;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    private static int[] FindProcessIdsAndDispose()
    {
        var processes = FindCodexDesktopProcesses();
        try
        {
            return processes.Select(process => process.Id).ToArray();
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static List<Process> FindCodexDesktopProcesses()
    {
        var result = new List<Process>();

        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            if (TryGetPackageFullName(process.Id, out var packageFullName) &&
                packageFullName.StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(process);
            }
            else
            {
                process.Dispose();
            }
        }

        return result;
    }

    private static string? FindApplicationUserModelId(IEnumerable<Process> processes)
    {
        foreach (var process in processes)
        {
            if (TryGetApplicationUserModelId(process.Id, out var applicationUserModelId) &&
                applicationUserModelId.EndsWith("!App", StringComparison.OrdinalIgnoreCase))
            {
                return applicationUserModelId;
            }
        }

        return null;
    }

    private static void RequestGracefulClose(HashSet<int> processIds)
    {
        EnumWindows((windowHandle, _) =>
        {
            GetWindowThreadProcessId(windowHandle, out var processId);
            if (processIds.Contains(unchecked((int)processId)))
                PostMessage(windowHandle, WmClose, IntPtr.Zero, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
    }

    private static bool IsStillRunning(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryGetPackageFullName(int processId, out string packageFullName)
    {
        packageFullName = string.Empty;
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return false;

        try
        {
            uint length = 0;
            var firstResult = GetPackageFullName(handle, ref length, null);
            if (firstResult != 122 || length == 0) return false;

            var buffer = new StringBuilder(unchecked((int)length));
            if (GetPackageFullName(handle, ref length, buffer) != 0) return false;
            packageFullName = buffer.ToString();
            return packageFullName.Length > 0;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static bool TryGetApplicationUserModelId(int processId, out string applicationUserModelId)
    {
        applicationUserModelId = string.Empty;
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return false;

        try
        {
            uint length = 0;
            var firstResult = GetApplicationUserModelId(handle, ref length, null);
            if (firstResult != 122 || length == 0) return false;

            var buffer = new StringBuilder(unchecked((int)length));
            if (GetApplicationUserModelId(handle, ref length, buffer) != 0) return false;
            applicationUserModelId = buffer.ToString();
            return applicationUserModelId.Length > 0;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private delegate bool EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFullName(IntPtr processHandle, ref uint packageFullNameLength, StringBuilder? packageFullName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetApplicationUserModelId(IntPtr processHandle, ref uint applicationUserModelIdLength, StringBuilder? applicationUserModelId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);
}

public sealed record CodexLaunchTarget(string ApplicationUserModelId, bool WasRunning);
