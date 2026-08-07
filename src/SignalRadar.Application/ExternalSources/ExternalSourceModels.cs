using System.Text.Json;

namespace SignalRadar.Application.ExternalSources;

public static class ExternalSourceTypes
{
    public const string GitHubReleases = "github-releases";
    public const string HackerNews = "hacker-news";
}

public sealed class ExternalSourceDefinition
{
    public ExternalSourceDefinition(
        string sourceType,
        string sourceKey,
        string displayName,
        Uri endpoint,
        TimeSpan pollingInterval,
        string settingsJson,
        bool enabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsJson);

        if (!endpoint.IsAbsoluteUri
            || endpoint.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "An external source endpoint must be an absolute HTTP or HTTPS URL.",
                nameof(endpoint));
        }

        if (pollingInterval < TimeSpan.FromMinutes(1)
            || pollingInterval > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollingInterval),
                "The polling interval must be between one minute and one day.");
        }

        using JsonDocument _ = JsonDocument.Parse(settingsJson);

        SourceType = ValidateLength(sourceType.Trim(), 100, nameof(sourceType));
        SourceKey = ValidateLength(sourceKey.Trim(), 500, nameof(sourceKey));
        DisplayName = ValidateLength(displayName.Trim(), 300, nameof(displayName));
        Endpoint = endpoint;
        PollingInterval = pollingInterval;
        SettingsJson = settingsJson;
        Enabled = enabled;
    }

    public string SourceType { get; }

    public string SourceKey { get; }

    public string DisplayName { get; }

    public Uri Endpoint { get; }

    public TimeSpan PollingInterval { get; }

    public string SettingsJson { get; }

    public bool Enabled { get; }

    private static string ValidateLength(string value, int maximum, string name)
    {
        if (value.Length > maximum)
        {
            throw new ArgumentOutOfRangeException(name);
        }

        return value;
    }
}

public sealed record ExternalSourceLease(
    Guid SourceId,
    Guid LeaseToken,
    string SourceType,
    string SourceKey,
    string DisplayName,
    Uri Endpoint,
    TimeSpan PollingInterval,
    string SettingsJson,
    string? ETag,
    DateTimeOffset? LastModified,
    int ConsecutiveFailures);

public sealed record ExternalArticleItem(
    string ExternalId,
    string Title,
    Uri Url,
    DateTimeOffset PublishedAt);

public sealed record ExternalFetchResult(
    bool NotModified,
    IReadOnlyList<ExternalArticleItem> Items,
    string? ETag,
    DateTimeOffset? LastModified)
{
    public static ExternalFetchResult Unchanged(
        string? etag,
        DateTimeOffset? lastModified)
    {
        return new ExternalFetchResult(true, [], etag, lastModified);
    }

    public static ExternalFetchResult Downloaded(
        IReadOnlyList<ExternalArticleItem> items,
        string? etag,
        DateTimeOffset? lastModified)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new ExternalFetchResult(false, items, etag, lastModified);
    }
}

public interface IExternalSourceCollector
{
    public string SourceType { get; }

    public ValueTask<ExternalFetchResult> FetchAsync(
        ExternalSourceLease source,
        CancellationToken cancellationToken);
}

public interface IExternalSourceStore
{
    public ValueTask UpsertDefinitionsAsync(
        IReadOnlyList<ExternalSourceDefinition> definitions,
        CancellationToken cancellationToken);

    public ValueTask<IReadOnlyList<ExternalSourceLease>> ClaimDueAsync(
        int maxCount,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    public ValueTask CompleteSuccessAsync(
        ExternalSourceLease source,
        string? etag,
        DateTimeOffset? lastModified,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    public ValueTask CompleteFailureAsync(
        ExternalSourceLease source,
        string error,
        DateTimeOffset failedAt,
        DateTimeOffset nextAttemptAt,
        DateTimeOffset? quarantineUntil,
        CancellationToken cancellationToken);
}

public enum ExternalCollectionStatus
{
    Success = 0,
    NotModified = 1,
    Failed = 2
}

public sealed record ExternalCollectionSummary(
    Guid SourceId,
    string SourceName,
    ExternalCollectionStatus Status,
    int ItemsSeen,
    int ArticlesAdded,
    int ArticlesDuplicate,
    string? Error = null);
