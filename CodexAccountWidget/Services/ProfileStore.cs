using System.Text.Json;
using System.IO;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using CodexAccountWidget.Models;

namespace CodexAccountWidget.Services;

public sealed class ProfileStore
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string AppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex-account-switcher");

    public string ProfilesRoot => Path.Combine(AppDataRoot, "profiles");
    public string SettingsPath => Path.Combine(AppDataRoot, "profiles.json");

    public async Task<ProfileSettings> LoadAsync()
    {
        Directory.CreateDirectory(AppDataRoot);
        HardenStoragePermissions();
        await MigrateLegacyDataIfNeededAsync();
        Directory.CreateDirectory(ProfilesRoot);

        if (!File.Exists(SettingsPath))
        {
            WriteLoadDiagnostic("설정 파일 없음", profileCount: 0);
            return new ProfileSettings();
        }

        Exception? lastException = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await using var stream = new FileStream(
                    SettingsPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                var settings = await JsonSerializer.DeserializeAsync<ProfileSettings>(stream, JsonOptions)
                               ?? new ProfileSettings();
                settings.Profiles ??= [];
                WriteLoadDiagnostic("로드 성공", settings.Profiles.Count);
                return settings;
            }
            catch (IOException exception) when (attempt < 3)
            {
                lastException = exception;
                await Task.Delay(120 * attempt);
            }
            catch (Exception exception)
            {
                lastException = exception;
                break;
            }
        }

        WriteLoadDiagnostic("로드 실패", profileCount: 0, lastException);
        return new ProfileSettings();
    }

    private void HardenStoragePermissions()
    {
        try
        {
            var currentUser = WindowsIdentity.GetCurrent().User
                              ?? throw new InvalidOperationException("현재 Windows 사용자 SID를 확인하지 못했습니다.");
            var inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                inheritance,
                PropagationFlags.None,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                inheritance,
                PropagationFlags.None,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                inheritance,
                PropagationFlags.None,
                AccessControlType.Allow));

            new DirectoryInfo(AppDataRoot).SetAccessControl(security);
        }
        catch (Exception exception)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(AppDataRoot, "permission-warning.log"),
                    $"시간: {DateTimeOffset.Now:O}{Environment.NewLine}" +
                    $"저장소 권한 강화 실패: {exception.GetType().Name}: {exception.Message}");
            }
            catch { }
        }
    }

    private async Task MigrateLegacyDataIfNeededAsync()
    {
        if (File.Exists(SettingsPath)) return;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var legacyRoots = new[]
        {
            Path.Combine(
                localAppData,
                "Packages",
                "OpenAI.Codex_2p2nqsd0c76g0",
                "LocalCache",
                "Local",
                "CodexAccountWidget"),
            Path.Combine(localAppData, "CodexAccountWidget")
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        var candidates = new List<(string Root, ProfileSettings Settings, DateTime Modified)>();
        foreach (var legacyRoot in legacyRoots)
        {
            var legacySettingsPath = Path.Combine(legacyRoot, "profiles.json");
            if (!File.Exists(legacySettingsPath)) continue;

            try
            {
                await using var stream = new FileStream(
                    legacySettingsPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                var settings = await JsonSerializer.DeserializeAsync<ProfileSettings>(stream, JsonOptions);
                if (settings is null) continue;
                settings.Profiles ??= [];
                candidates.Add((legacyRoot, settings, File.GetLastWriteTimeUtc(legacySettingsPath)));
            }
            catch
            {
                // 다른 후보 저장소를 계속 확인합니다.
            }
        }

        var selected = candidates
            .OrderByDescending(candidate => candidate.Settings.Profiles.Count)
            .ThenByDescending(candidate => candidate.Modified)
            .FirstOrDefault();
        if (selected.Settings is null) return;

        Directory.CreateDirectory(ProfilesRoot);
        var sourceProfilesRoot = Path.Combine(selected.Root, "profiles");
        if (Directory.Exists(sourceProfilesRoot))
        {
            foreach (var sourceDirectory in Directory.GetDirectories(sourceProfilesRoot))
            {
                var destinationDirectory = Path.Combine(ProfilesRoot, Path.GetFileName(sourceDirectory));
                CopyDirectory(sourceDirectory, destinationDirectory);
            }
        }

        foreach (var profile in selected.Settings.Profiles)
        {
            var sourceAuth = Path.Combine(sourceProfilesRoot, profile.Id, "auth.json");
            var destinationHome = Path.Combine(ProfilesRoot, profile.Id);
            var destinationAuth = Path.Combine(destinationHome, "auth.json");

            if (File.Exists(sourceAuth))
            {
                if (!File.Exists(destinationAuth) || !FilesMatch(sourceAuth, destinationAuth))
                    throw new IOException($"계정 {profile.Id}의 인증파일 복사 검증에 실패했습니다.");
            }

            profile.HomePath = destinationHome;
        }

        await SaveAsync(selected.Settings);
        WriteMigrationDiagnostic(selected.Root, selected.Settings.Profiles.Count);
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);

        foreach (var directory in Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(destinationRoot, relativePath));
        }

        foreach (var file in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, file);
            var destination = Path.Combine(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static bool FilesMatch(string leftPath, string rightPath)
    {
        using var left = File.OpenRead(leftPath);
        using var right = File.OpenRead(rightPath);
        return SHA256.HashData(left).SequenceEqual(SHA256.HashData(right));
    }

    private void WriteMigrationDiagnostic(string sourceRoot, int profileCount)
    {
        try
        {
            File.WriteAllLines(Path.Combine(AppDataRoot, "migration.log"),
            [
                $"시간: {DateTimeOffset.Now:O}",
                $"이전 저장소: {sourceRoot}",
                $"새 저장소: {AppDataRoot}",
                $"이전 계정 수: {profileCount}",
                "기존 저장소는 삭제하지 않았습니다."
            ]);
        }
        catch { }
    }

    private void WriteLoadDiagnostic(string result, int profileCount, Exception? exception = null)
    {
        try
        {
            var lines = new List<string>
            {
                $"시간: {DateTimeOffset.Now:O}",
                $"결과: {result}",
                $"설정 경로: {SettingsPath}",
                $"로드 계정 수: {profileCount}"
            };
            if (exception is not null)
                lines.Add($"오류: {exception.GetType().Name}: {exception.Message}");

            File.WriteAllLines(Path.Combine(AppDataRoot, "profile-load.log"), lines);
        }
        catch { }
    }

    public async Task SaveAsync(ProfileSettings settings)
    {
        await _saveLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(AppDataRoot);
            var temporaryPath = SettingsPath + ".tmp";

            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
            }

            File.Move(temporaryPath, SettingsPath, true);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public AccountProfile CreatePendingProfile()
    {
        var id = Guid.NewGuid().ToString("N");
        var home = Path.Combine(ProfilesRoot, id);
        Directory.CreateDirectory(home);

        return new AccountProfile
        {
            Id = id,
            HomePath = home,
            DisplayName = "새 Codex 계정",
            Status = "로그인 대기 중"
        };
    }

    public void RemoveProfileDirectory(AccountProfile profile)
    {
        var root = Path.GetFullPath(ProfilesRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(profile.HomePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("프로필 폴더가 앱 데이터 영역 밖에 있습니다.");

        if (Directory.Exists(profile.HomePath)) Directory.Delete(profile.HomePath, true);
    }
}

public sealed class ProfileSettings
{
    public string? ActiveProfileId { get; set; }
    public List<AccountProfile> Profiles { get; set; } = [];
    public bool ShowOnlyWhileCodexIsRunning { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool AutoAdjustWidgetTextColor { get; set; } = true;
}
