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
    private const int LaunchAttemptCount = 2;
    private static readonly TimeSpan LaunchAttemptTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan StopStabilityDuration = TimeSpan.FromMilliseconds(900);

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

        // 패키지 앱은 주 프로세스가 종료된 직후 보조 프로세스를 다시 만들 수 있습니다.
        // 일정 시간 동안 관련 프로세스가 하나도 없는 상태를 확인한 뒤 인증파일을 교체합니다.
        await WaitUntilFullyStoppedAsync(cancellationToken);

        return new CodexLaunchTarget(applicationUserModelId, WasRunning: true);
    }

    public async Task StartAsync(CodexLaunchTarget target, CancellationToken cancellationToken = default)
    {
        await Task.Delay(350, cancellationToken);
        var sawProcess = false;

        for (var attempt = 1; attempt <= LaunchAttemptCount; attempt++)
        {
            ActivatePackagedApplication(target.ApplicationUserModelId);
            var deadline = DateTime.UtcNow + LaunchAttemptTimeout;

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (HasRunningWindow()) return;

                if (FindProcessIdsAndDispose().Length > 0)
                    sawProcess = true;

                await Task.Delay(250, cancellationToken);
            }

            if (attempt < LaunchAttemptCount)
                await Task.Delay(750, cancellationToken);
        }

        throw new InvalidOperationException(sawProcess
            ? "Codex 프로세스는 시작됐지만 두 번의 실행 시도 후에도 표시 창이 열리지 않았습니다."
            : "Codex를 두 번 실행했지만 앱 프로세스가 확인되지 않았습니다.");
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

    private static async Task WaitUntilFullyStoppedAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        DateTime? stableSince = null;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = FindCodexDesktopProcesses();

            if (remaining.Count == 0)
            {
                stableSince ??= DateTime.UtcNow;
                if (DateTime.UtcNow - stableSince >= StopStabilityDuration) return;
            }
            else
            {
                stableSince = null;
                foreach (var process in remaining)
                {
                    try
                    {
                        if (!process.HasExited) process.Kill(entireProcessTree: false);
                    }
                    catch (InvalidOperationException)
                    {
                        // 종료 확인과 동시에 사라진 프로세스입니다.
                    }
                    catch (Win32Exception exception)
                    {
                        throw new InvalidOperationException(
                            $"남아 있는 Codex 프로세스({process.Id})를 종료하지 못했습니다: {exception.Message}", exception);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }

            await Task.Delay(200, cancellationToken);
        }

        throw new InvalidOperationException("Codex 프로세스가 완전히 종료되지 않아 계정 전환을 중단했습니다.");
    }

    private static void ActivatePackagedApplication(string applicationUserModelId)
    {
        try
        {
            var managerType = Type.GetTypeFromCLSID(
                                  new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"),
                                  throwOnError: true)
                              ?? throw new COMException("Windows 앱 활성화 관리자를 찾지 못했습니다.");
            var manager = (IApplicationActivationManager)(Activator.CreateInstance(managerType)
                          ?? throw new COMException("Windows 앱 활성화 관리자를 만들지 못했습니다."));
            try
            {
                var result = manager.ActivateApplication(
                    applicationUserModelId,
                    null,
                    ActivateOptions.None,
                    out _);
                Marshal.ThrowExceptionForHR(result);
                return;
            }
            finally
            {
                Marshal.FinalReleaseComObject(manager);
            }
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or PlatformNotSupportedException)
        {
            // 일부 Windows 환경에서는 활성화 관리자를 사용할 수 없어 기존 셸 경로로 대체합니다.
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"shell:AppsFolder\\{applicationUserModelId}",
            UseShellExecute = true
        };

        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Codex 데스크톱 앱 실행 요청을 보내지 못했습니다.");
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

    [Flags]
    private enum ActivateOptions
    {
        None = 0
    }

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string applicationUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            ActivateOptions options,
            out uint processId);

        void ActivateForFile();
        void ActivateForProtocol();
    }
}

public sealed record CodexLaunchTarget(string ApplicationUserModelId, bool WasRunning);
