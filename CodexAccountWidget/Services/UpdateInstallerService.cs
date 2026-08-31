using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace CodexAccountWidget.Services;

public sealed class UpdateInstallerService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };

    public async Task<string> DownloadVerifiedAsync(
        UpdateCheckResult update,
        CancellationToken cancellationToken = default)
    {
        var checksumText = await Client.GetStringAsync(update.ChecksumUri, cancellationToken);
        var expectedHash = ParseInstallerHash(checksumText);
        var updateDirectory = Path.Combine(
            Path.GetTempPath(), "CodexAccountSwitcher", update.LatestTagName);
        Directory.CreateDirectory(updateDirectory);
        var installerPath = Path.Combine(updateDirectory, "CodexAccountSwitcher-Setup.exe");

        await using (var source = await Client.GetStreamAsync(update.InstallerUri, cancellationToken))
        await using (var destination = File.Create(installerPath))
            await source.CopyToAsync(destination, cancellationToken);

        await using var installer = File.OpenRead(installerPath);
        var actualHash = Convert.ToHexString(
            await SHA256.HashDataAsync(installer, cancellationToken));
        if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(installerPath);
            throw new InvalidDataException("설치 파일의 SHA-256 검증에 실패했습니다.");
        }

        return installerPath;
    }

    public void Launch(string installerPath)
    {
        _ = Process.Start(new ProcessStartInfo(installerPath)
        {
            UseShellExecute = true,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART " +
                        "/CLOSEAPPLICATIONS /RESTARTAPPLICATIONS"
        }) ?? throw new InvalidOperationException("업데이트 설치 프로그램을 시작하지 못했습니다.");
    }

    internal static string ParseInstallerHash(string checksumText)
    {
        var line = checksumText.Split('\n').SingleOrDefault(value =>
            value.TrimEnd('\r').EndsWith(
                "  CodexAccountSwitcher-Setup.exe", StringComparison.Ordinal));
        var hash = line?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (hash is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new InvalidDataException("설치 파일 체크섬을 찾지 못했습니다.");
        return hash;
    }
}
