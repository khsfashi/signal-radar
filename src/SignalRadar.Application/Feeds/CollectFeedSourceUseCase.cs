using SignalRadar.Application.Articles;

namespace SignalRadar.Application.Feeds;

public sealed class CollectFeedSourceUseCase
{
    private readonly IFeedSourceStore _sourceStore;
    private readonly IFeedDocumentFetcher _fetcher;
    private readonly IFeedDocumentParser _parser;
    private readonly CollectArticleUseCase _collectArticle;
    private readonly TimeProvider _timeProvider;

    public CollectFeedSourceUseCase(
        IFeedSourceStore sourceStore,
        IFeedDocumentFetcher fetcher,
        IFeedDocumentParser parser,
        CollectArticleUseCase collectArticle,
        TimeProvider timeProvider)
    {
        _sourceStore = sourceStore ?? throw new ArgumentNullException(nameof(sourceStore));
        _fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _collectArticle = collectArticle ?? throw new ArgumentNullException(nameof(collectArticle));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async ValueTask<FeedCollectionSummary> ExecuteAsync(
        FeedSourceLease source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            FeedFetchResult fetchResult = await _fetcher
                .FetchAsync(source, cancellationToken)
                .ConfigureAwait(false);
            DateTimeOffset completedAt = _timeProvider.GetUtcNow();

            if (fetchResult.Status == FeedFetchStatus.NotModified)
            {
                await _sourceStore
                    .CompleteSuccessAsync(
                        source,
                        fetchResult.ETag,
                        fetchResult.LastModified,
                        completedAt,
                        cancellationToken)
                    .ConfigureAwait(false);

                return new FeedCollectionSummary(
                    source.SourceId,
                    source.Name,
                    FeedCollectionStatus.NotModified,
                    0,
                    0,
                    0);
            }

            IReadOnlyList<ParsedFeedItem> items = _parser.Parse(
                fetchResult.Content,
                source.FeedUrl,
                completedAt);
            int articlesAdded = 0;
            int articlesDuplicate = 0;

            for (int index = 0; index < items.Count; index++)
            {
                ParsedFeedItem item = items[index];
                CollectedArticleCandidate candidate = new(
                    item.Title,
                    item.Url.AbsoluteUri,
                    source.Name,
                    item.PublishedAt);
                CollectArticleResult articleResult = await _collectArticle
                    .ExecuteAsync(candidate, cancellationToken)
                    .ConfigureAwait(false);

                if (articleResult.Status == CollectArticleStatus.Added)
                {
                    articlesAdded++;
                }
                else
                {
                    articlesDuplicate++;
                }
            }

            await _sourceStore
                .CompleteSuccessAsync(
                    source,
                    fetchResult.ETag,
                    fetchResult.LastModified,
                    completedAt,
                    cancellationToken)
                .ConfigureAwait(false);

            return new FeedCollectionSummary(
                source.SourceId,
                source.Name,
                FeedCollectionStatus.Success,
                items.Count,
                articlesAdded,
                articlesDuplicate);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            DateTimeOffset failedAt = _timeProvider.GetUtcNow();
            FeedFailureSchedule failureSchedule = FeedFailureSchedule.Calculate(
                source.ConsecutiveFailures + 1,
                failedAt);
            string error = NormalizeError(exception);

            await _sourceStore
                .CompleteFailureAsync(
                    source,
                    error,
                    failedAt,
                    failureSchedule.NextAttemptAt,
                    failureSchedule.QuarantineUntil,
                    CancellationToken.None)
                .ConfigureAwait(false);

            return new FeedCollectionSummary(
                source.SourceId,
                source.Name,
                FeedCollectionStatus.Failed,
                0,
                0,
                0,
                error);
        }
    }

    private static string NormalizeError(Exception exception)
    {
        string error = $"{exception.GetType().Name}: {exception.Message}";
        return error.Length <= 2000
            ? error
            : error[..2000];
    }
}

public sealed record FeedFailureSchedule(
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? QuarantineUntil)
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1)
    ];

    public static FeedFailureSchedule Calculate(
        int consecutiveFailures,
        DateTimeOffset failedAt)
    {
        if (consecutiveFailures <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(consecutiveFailures));
        }

        if (consecutiveFailures >= 5)
        {
            DateTimeOffset quarantineUntil = failedAt.AddHours(6);
            return new FeedFailureSchedule(
                quarantineUntil,
                quarantineUntil);
        }

        TimeSpan retryDelay = RetryDelays[consecutiveFailures - 1];
        return new FeedFailureSchedule(
            failedAt.Add(retryDelay),
            null);
    }
}
