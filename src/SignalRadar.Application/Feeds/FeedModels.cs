using SignalRadar.Application.Articles;

namespace SignalRadar.Application.Feeds;

public sealed class FeedSourceDefinition
{
    public FeedSourceDefinition(
        string name,
        Uri feedUrl,
        TimeSpan pollingInterval,
        bool enabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(feedUrl);

        if (!feedUrl.IsAbsoluteUri
            || feedUrl.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "A feed URL must be an absolute HTTP or HTTPS URL.",
                nameof(feedUrl));
        }

        if (pollingInterval < TimeSpan.FromMinutes(1)
            || pollingInterval > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(pollingInterval),
                "The polling interval must be between one minute and one day.");
        }

        Name = name.Trim();
        FeedUrl = feedUrl;
        PollingInterval = pollingInterval;
        Enabled = enabled;
    }

    public string Name { get; }

    public Uri FeedUrl { get; }

    public TimeSpan PollingInterval { get; }

    public bool Enabled { get; }
}

public sealed record FeedSourceLease(
    Guid SourceId,
    Guid LeaseToken,
    string Name,
    Uri FeedUrl,
    TimeSpan PollingInterval,
    string? ETag,
    DateTimeOffset? LastModified,
    int ConsecutiveFailures);

public enum FeedFetchStatus
{
    Downloaded = 0,
    NotModified = 1
}

public sealed record FeedFetchResult(
    FeedFetchStatus Status,
    byte[] Content,
    string? ETag,
    DateTimeOffset? LastModified)
{
    public static FeedFetchResult Downloaded(
        byte[] content,
        string? etag,
        DateTimeOffset? lastModified)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new FeedFetchResult(
            FeedFetchStatus.Downloaded,
            content,
            etag,
            lastModified);
    }

    public static FeedFetchResult NotModified(
        string? etag,
        DateTimeOffset? lastModified)
    {
        return new FeedFetchResult(
            FeedFetchStatus.NotModified,
            [],
            etag,
            lastModified);
    }
}

public sealed record ParsedFeedItem(
    string Title,
    Uri Url,
    DateTimeOffset PublishedAt);

public interface IFeedDocumentFetcher
{
    public ValueTask<FeedFetchResult> FetchAsync(
        FeedSourceLease source,
        CancellationToken cancellationToken);
}

public interface IFeedDocumentParser
{
    public IReadOnlyList<ParsedFeedItem> Parse(
        byte[] content,
        Uri feedUrl,
        DateTimeOffset fetchedAt);
}

public interface IFeedSourceStore
{
    public ValueTask UpsertDefinitionsAsync(
        IReadOnlyList<FeedSourceDefinition> definitions,
        CancellationToken cancellationToken);

    public ValueTask<IReadOnlyList<FeedSourceLease>> ClaimDueAsync(
        int maxCount,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    public ValueTask CompleteSuccessAsync(
        FeedSourceLease source,
        string? etag,
        DateTimeOffset? lastModified,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken);

    public ValueTask CompleteFailureAsync(
        FeedSourceLease source,
        string error,
        DateTimeOffset failedAt,
        DateTimeOffset nextAttemptAt,
        DateTimeOffset? quarantineUntil,
        CancellationToken cancellationToken);
}

public enum FeedCollectionStatus
{
    Success = 0,
    NotModified = 1,
    Failed = 2
}

public sealed record FeedCollectionSummary(
    Guid SourceId,
    string SourceName,
    FeedCollectionStatus Status,
    int ItemsSeen,
    int ArticlesAdded,
    int ArticlesDuplicate,
    string? Error = null);
