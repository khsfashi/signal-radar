using System.Text.Json;
using SignalRadar.Application.Feeds;

namespace SignalRadar.Infrastructure.Feeds;

public sealed class FeedSourceConfigurationLoader
{
    private const int MaximumConfigurationBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public async ValueTask<IReadOnlyList<FeedSourceDefinition>> LoadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        FileInfo file = new(path);

        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "The feed source configuration file was not found.",
                file.FullName);
        }

        if (file.Length is <= 0 or > MaximumConfigurationBytes)
        {
            throw new InvalidDataException(
                "The feed source configuration file has an invalid size.");
        }

        await using FileStream stream = new(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            useAsync: true);
        FeedSourceConfigurationEntry?[] entries = await JsonSerializer
            .DeserializeAsync<FeedSourceConfigurationEntry?[]>(
                stream,
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException(
                "The feed source configuration file was empty.");

        if (entries.Length == 0)
        {
            throw new InvalidDataException(
                "At least one feed source must be configured.");
        }

        List<FeedSourceDefinition> definitions = new(entries.Length);
        HashSet<string> urls = new(StringComparer.Ordinal);

        for (int index = 0; index < entries.Length; index++)
        {
            FeedSourceConfigurationEntry? entry = entries[index];

            if (entry is null
                || string.IsNullOrWhiteSpace(entry.Name)
                || string.IsNullOrWhiteSpace(entry.Url)
                || entry.PollIntervalMinutes is < 1 or > 1440
                || !Uri.TryCreate(entry.Url, UriKind.Absolute, out Uri? feedUrl)
                || feedUrl.Scheme is not ("http" or "https"))
            {
                throw new InvalidDataException(
                    $"Feed source entry {index} is invalid.");
            }

            string canonicalUrl = feedUrl.AbsoluteUri;

            if (!urls.Add(canonicalUrl))
            {
                throw new InvalidDataException(
                    $"Feed source URL '{canonicalUrl}' is duplicated.");
            }

            definitions.Add(new FeedSourceDefinition(
                entry.Name,
                feedUrl,
                TimeSpan.FromMinutes(entry.PollIntervalMinutes),
                entry.Enabled));
        }

        return definitions;
    }
}

internal sealed class FeedSourceConfigurationEntry
{
    public FeedSourceConfigurationEntry()
    {
    }

    public string? Name { get; init; }

    public string? Url { get; init; }

    public int PollIntervalMinutes { get; init; } = 15;

    public bool Enabled { get; init; } = true;
}
