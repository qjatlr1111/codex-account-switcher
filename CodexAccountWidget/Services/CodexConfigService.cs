using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CodexAccountWidget.Models;

namespace CodexAccountWidget.Services;

public sealed partial class CodexConfigService
{
    private const string ManagedComment = " # Codex Account Switcher: 계정 사용 중";

    public string ConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex",
        "config.toml");

    public async Task<CodexProviderConfiguration> LoadAsync()
    {
        if (!File.Exists(ConfigPath)) return new CodexProviderConfiguration([], null);

        var lines = await File.ReadAllLinesAsync(ConfigPath);
        var providers = new List<ModelProviderOption>();
        string? activeProviderId = null;
        string? currentProviderId = null;
        var currentProviderIndex = -1;

        foreach (var line in lines)
        {
            var header = ProviderTableHeaderRegex().Match(line);
            if (header.Success)
            {
                currentProviderId = ParseTomlStringOrBareKey(header.Groups["id"].Value);
                providers.Add(new ModelProviderOption(currentProviderId, currentProviderId));
                currentProviderIndex = providers.Count - 1;
                continue;
            }

            if (AnyTableHeaderRegex().IsMatch(line))
            {
                currentProviderId = null;
                currentProviderIndex = -1;
                continue;
            }

            if (currentProviderId is not null)
            {
                var name = ProviderNameRegex().Match(line);
                if (name.Success)
                {
                    providers[currentProviderIndex] = new ModelProviderOption(
                        currentProviderId,
                        ParseTomlString(name.Groups["value"].Value));
                }

                continue;
            }

            var selection = ActiveProviderRegex().Match(line);
            if (selection.Success)
                activeProviderId = ParseTomlString(selection.Groups["value"].Value);
        }

        return new CodexProviderConfiguration(providers, activeProviderId);
    }

    public Task SelectProviderAsync(string providerId) => RewriteSelectionAsync(providerId);

    public Task DisableProviderForAccountAsync() => RewriteSelectionAsync(null);

    private async Task RewriteSelectionAsync(string? providerId)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        var lines = File.Exists(ConfigPath)
            ? (await File.ReadAllLinesAsync(ConfigPath)).ToList()
            : [];
        var activeIndex = lines.FindIndex(line => ActiveProviderRegex().IsMatch(line));

        if (providerId is null)
        {
            if (activeIndex >= 0)
                lines[activeIndex] = "# " + lines[activeIndex].TrimStart() + ManagedComment;
        }
        else
        {
            var assignment = $"model_provider = {QuoteTomlString(providerId)}";
            if (activeIndex >= 0)
            {
                lines[activeIndex] = assignment;
            }
            else
            {
                var commentedIndex = lines.FindIndex(line => ManagedProviderRegex().IsMatch(line));
                if (commentedIndex >= 0)
                {
                    lines[commentedIndex] = assignment;
                }
                else
                {
                    var firstTableIndex = lines.FindIndex(line => AnyTableHeaderRegex().IsMatch(line));
                    lines.Insert(firstTableIndex >= 0 ? firstTableIndex : lines.Count, assignment);
                }
            }
        }

        await WriteAtomicallyAsync(lines);
    }

    private async Task WriteAtomicallyAsync(List<string> lines)
    {
        var temporaryPath = ConfigPath + ".widget-tmp";
        await File.WriteAllLinesAsync(temporaryPath, lines, new UTF8Encoding(false));
        File.Move(temporaryPath, ConfigPath, true);
    }

    private static string ParseTomlStringOrBareKey(string value) =>
        value.StartsWith('"') || value.StartsWith('\'') ? ParseTomlString(value) : value;

    private static string ParseTomlString(string value)
    {
        var content = value[1..^1];
        return value[0] == '\''
            ? content
            : content.Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    private static string QuoteTomlString(string value) =>
        $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    [GeneratedRegex("^\\s*\\[\\s*model_providers\\.(?<id>[A-Za-z0-9_-]+|\"(?:[^\"\\\\]|\\\\.)*\"|'[^']*')\\s*\\]\\s*(?:#.*)?$")]
    private static partial Regex ProviderTableHeaderRegex();

    [GeneratedRegex("^\\s*\\[")]
    private static partial Regex AnyTableHeaderRegex();

    [GeneratedRegex("^\\s*name\\s*=\\s*(?<value>\"(?:[^\"\\\\]|\\\\.)*\"|'[^']*')\\s*(?:#.*)?$")]
    private static partial Regex ProviderNameRegex();

    [GeneratedRegex("^\\s*model_provider\\s*=\\s*(?<value>\"(?:[^\"\\\\]|\\\\.)*\"|'[^']*')\\s*(?:#.*)?$")]
    private static partial Regex ActiveProviderRegex();

    [GeneratedRegex("^\\s*#\\s*model_provider\\s*=.*# Codex Account Switcher: 계정 사용 중\\s*$")]
    private static partial Regex ManagedProviderRegex();
}

public sealed record CodexProviderConfiguration(
    IReadOnlyList<ModelProviderOption> Providers,
    string? ActiveProviderId);
