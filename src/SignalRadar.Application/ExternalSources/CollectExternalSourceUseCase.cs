using SignalRadar.Application.Articles;

namespace SignalRadar.Application.ExternalSources;

public sealed class CollectExternalSourceUseCase
{
    private readonly IExternalSourceStore _sourceStore;
    private readonly IReadOnlyDictionary<string, IExternalSourceCollector> _collectors;
    private readonly CollectArticleUseCase _collectArticle;
    private readonly TimeProvider _timeProvider;

    public CollectExternalSourceUseCase(
        IExternalSourceStore sourceStore,
        IReadOnlyCollection<IExternalSourceCollector> collectors,
        CollectArticleUseCase collectArticle,
        TimeProvider timeProvider)
    {
        _sourceStore = sourceStore
            ?? throw new ArgumentNullException(nameof(sourceStore));
        ArgumentNullException.ThrowIfNull(collectors);
        _collectArticle = collectArticle
            ?? throw new ArgumentNullException(nameof(collectArticle));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));

        Dictionary<string, IExternalSourceCollector> collectorMap =
            new(StringComparer.Ordinal);

        foreach (IExternalSourceCollector collector in collectors)
        {
            if (!collectorMap.TryAdd(collector.SourceType, collector))
            {
                throw new ArgumentException(
                    $"Collector type '{collector.SourceType}' is registered more than once.",
                    nameof(collectors));
            }
        }

        _collectors = collectorMap;
    }

    public async ValueTask<ExternalCollectionSummary> ExecuteAsync(
        ExternalSourceLease source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!_collectors.TryGetValue(
                source.SourceType,
                out IExternalSourceCollector? collector))
        {
            throw new InvalidOperationException(
                $"No collector is registered for source type '{source.SourceType}'.");
        }

        try
        {
            ExternalFetchResult fetchResult = await collector
                .FetchAsync(source, cancellationToken)
                .ConfigureAwait(false);
            DateTimeOffset completedAt = _timeProvider.GetUtcNow();

            if (fetchResult.NotModified)
            {
                await CompleteSuccessAsync(
                    source,
                    fetchResult,
                    completedAt,
                    cancellationToken).ConfigureAwait(false);

                return new ExternalCollectionSummary(
                    source.SourceId,
                    source.DisplayName,
                    ExternalCollectionStatus.NotModified,
                    0,
                    0,
                    0);
            }

            int articlesAdded = 0;
            int articlesDuplicate = 0;

            for (int index = 0; index < fetchResult.Items.Count; index++)
            {
                ExternalArticleItem item = fetchResult.Items[index];
                CollectedArticleCandidate candidate = new(
                    item.Title,
                    item.Url.AbsoluteUri,
                    source.SourceKey,
                    item.PublishedAt,
                    item.ExternalId);
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

            await CompleteSuccessAsync(
                source,
                fetchResult,
                completedAt,
                cancellationToken).ConfigureAwait(false);

            return new ExternalCollectionSummary(
                source.SourceId,
                source.DisplayName,
                ExternalCollectionStatus.Success,
                fetchResult.Items.Count,
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
            ExternalFailureSchedule schedule = ExternalFailureSchedule.Calculate(
                source.ConsecutiveFailures + 1,
                failedAt);
            string error = NormalizeError(exception);

            await _sourceStore
                .CompleteFailureAsync(
                    source,
                    error,
                    failedAt,
                    schedule.NextAttemptAt,
                    schedule.QuarantineUntil,
                    CancellationToken.None)
                .ConfigureAwait(false);

            return new ExternalCollectionSummary(
                source.SourceId,
                source.DisplayName,
                ExternalCollectionStatus.Failed,
                0,
                0,
                0,
                error);
        }
    }

    private ValueTask CompleteSuccessAsync(
        ExternalSourceLease source,
        ExternalFetchResult result,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        return _sourceStore.CompleteSuccessAsync(
            source,
            result.ETag,
            result.LastModified,
            completedAt,
            cancellationToken);
    }

    private static string NormalizeError(Exception exception)
    {
        string error = $"{exception.GetType().Name}: {exception.Message}";
        return error.Length <= 2000
            ? error
            : error[..2000];
    }
}

public sealed record ExternalFailureSchedule(
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

    public static ExternalFailureSchedule Calculate(
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
            return new ExternalFailureSchedule(
                quarantineUntil,
                quarantineUntil);
        }

        TimeSpan retryDelay = RetryDelays[consecutiveFailures - 1];
        return new ExternalFailureSchedule(
            failedAt.Add(retryDelay),
            null);
    }
}
