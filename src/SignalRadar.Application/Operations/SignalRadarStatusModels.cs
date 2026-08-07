namespace SignalRadar.Application.Operations;

public sealed record SignalRadarStatusSnapshot(
    DateTimeOffset CheckedAt,
    DateTimeOffset StartedAt,
    long ArticleCount,
    DateTimeOffset? LatestCollectedAt,
    int EnabledFeedCount,
    int EnabledExternalSourceCount,
    long SavedArticleCount,
    long SummaryCacheCount,
    long ArticleContentCacheCount,
    string RankingProfileVersion,
    string SummaryProvider,
    bool DigestSchedulerEnabled);

public interface ISignalRadarStatusReader
{
    public ValueTask<SignalRadarStatusSnapshot> ReadAsync(
        CancellationToken cancellationToken);
}
