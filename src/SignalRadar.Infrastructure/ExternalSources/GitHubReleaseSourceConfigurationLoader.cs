using System.Text.Json;
using System.Text.RegularExpressions;
using SignalRadar.Application.ExternalSources;

namespace SignalRadar.Infrastructure.ExternalSources;

public sealed record GitHubReleaseSettings(bool IncludePrereleases);

public sealed partial class GitHubReleaseSourceConfigurationLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async ValueTask<IReadOnlyList<ExternalSourceDefinition>> LoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        GitHubRepositoryConfiguration[] configurations =
            await JsonSerializer.DeserializeAsync<GitHubRepositoryConfiguration[]>(
                stream,
                SerializerOptions,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException(
                "The GitHub repository configuration is empty.");
        List<ExternalSourceDefinition> definitions = new(configurations.Length);
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < configurations.Length; index++)
        {
            GitHubRepositoryConfiguration configuration = configurations[index];
            ValidateRepositoryPart(configuration.Owner, "owner");
            ValidateRepositoryPart(configuration.Repository, "repository");

            int pollingMinutes = configuration.PollIntervalMinutes ?? 30;

            if (pollingMinutes is < 1 or > 1440)
            {
                throw new InvalidDataException(
                    "GitHub pollIntervalMinutes must be between 1 and 1440.");
            }

            string sourceKey =
                $"github:{configuration.Owner}/{configuration.Repository}";

            if (!keys.Add(sourceKey))
            {
                throw new InvalidDataException(
                    $"GitHub repository '{sourceKey}' is configured more than once.");
            }

            string displayName = string.IsNullOrWhiteSpace(configuration.DisplayName)
                ? $"{configuration.Owner}/{configuration.Repository}"
                : configuration.DisplayName.Trim();
            Uri endpoint = new(
                $"https://api.github.com/repos/{configuration.Owner}/{configuration.Repository}/releases?per_page=30");
            string settingsJson = JsonSerializer.Serialize(
                new GitHubReleaseSettings(
                    configuration.IncludePrereleases ?? false));

            definitions.Add(new ExternalSourceDefinition(
                ExternalSourceTypes.GitHubReleases,
                sourceKey,
                displayName,
                endpoint,
                TimeSpan.FromMinutes(pollingMinutes),
                settingsJson,
                configuration.Enabled ?? true));
        }

        return definitions;
    }

    private static void ValidateRepositoryPart(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !RepositoryPartRegex().IsMatch(value))
        {
            throw new InvalidDataException(
                $"GitHub {field} contains unsupported characters.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPartRegex();

    private sealed class GitHubRepositoryConfiguration
    {
        public string? Owner { get; init; }

        public string? Repository { get; init; }

        public string? DisplayName { get; init; }

        public int? PollIntervalMinutes { get; init; }

        public bool? IncludePrereleases { get; init; }

        public bool? Enabled { get; init; }
    }
}
