using System.IO;

namespace ClaudeAccountWidget.Services;

/// <summary>
/// Claude Code가 사용하는 설정·인증 파일 위치입니다.
/// CLAUDE_CONFIG_DIR이 지정되지 않으면 인증은 %USERPROFILE%\.claude\.credentials.json,
/// 계정 정보(oauthAccount)는 %USERPROFILE%\.claude.json에 저장됩니다.
/// CLAUDE_CONFIG_DIR이 지정되면 두 파일 모두 그 폴더 안에 저장됩니다.
/// </summary>
public static class ClaudePaths
{
    public const string CredentialsFileName = ".credentials.json";
    public const string GlobalConfigFileName = ".claude.json";

    private static string UserProfile =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string? CustomMainConfigDir
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            return string.IsNullOrWhiteSpace(value) ? null : Environment.ExpandEnvironmentVariables(value);
        }
    }

    /// <summary>기본(현재 사용 중인) Claude Code 설정 폴더입니다.</summary>
    public static string MainConfigDir => CustomMainConfigDir ?? Path.Combine(UserProfile, ".claude");

    public static string MainCredentialsPath => Path.Combine(MainConfigDir, CredentialsFileName);

    public static string MainGlobalConfigPath => CustomMainConfigDir is { } custom
        ? Path.Combine(custom, GlobalConfigFileName)
        : Path.Combine(UserProfile, GlobalConfigFileName);

    /// <summary>위젯 전용 프로필 폴더(CLAUDE_CONFIG_DIR로 사용)의 인증 파일입니다.</summary>
    public static string ProfileCredentialsPath(string profileHome) =>
        Path.Combine(profileHome, CredentialsFileName);

    public static string ProfileGlobalConfigPath(string profileHome) =>
        Path.Combine(profileHome, GlobalConfigFileName);
}
