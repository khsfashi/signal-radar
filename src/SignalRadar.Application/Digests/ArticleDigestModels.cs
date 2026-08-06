using SignalRadar.Application.Articles;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Application.Digests;

public enum ArticleDigestPeriod
{
    Daily,
    Weekly
}

public sealed record ArticleDigest(
    ArticleDigestPeriod Period,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    ArticleTopic Topic,
    IReadOnlyList<RankedArticle> Articles);

public sealed record DigestDeliveryLease(
    string DeliveryKey,
    DateTimeOffset WindowStart,
    Guid LeaseToken);

public interface IDigestDeliveryReceiptStore
{
    public ValueTask<DigestDeliveryLease?> TryBeginAsync(
        string deliveryKey,
        DateTimeOffset windowStart,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    public ValueTask CompleteAsync(
        DigestDeliveryLease lease,
        DateTimeOffset deliveredAt,
        ulong? discordMessageId,
        CancellationToken cancellationToken);

    public ValueTask FailAsync(
        DigestDeliveryLease lease,
        DateTimeOffset failedAt,
        string error,
        CancellationToken cancellationToken);
}

public sealed class GenerateArticleDigestUseCase
{
    private const int MaximumArticles = 10;
    private readonly IArticleRankingReader _rankingReader;
    private readonly TimeProvider _timeProvider;

    public GenerateArticleDigestUseCase(
        IArticleRankingReader rankingReader,
        TimeProvider timeProvider)
    {
        _rankingReader = rankingReader
            ?? throw new ArgumentNullException(nameof(rankingReader));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async ValueTask<ArticleDigest> GenerateAsync(
        string actorId,
        ArticleDigestPeriod period,
        ArticleTopic topic,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);

        if (limit is < 1 or > MaximumArticles)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        TimeSpan duration = period switch
        {
            ArticleDigestPeriod.Daily => TimeSpan.FromDays(1),
            ArticleDigestPeriod.Weekly => TimeSpan.FromDays(7),
            _ => throw new ArgumentOutOfRangeException(nameof(period))
        };
        DateTimeOffset windowEnd = _timeProvider.GetUtcNow();
        DateTimeOffset windowStart = windowEnd.Subtract(duration);
        IReadOnlyList<RankedArticle> articles = await _rankingReader.GetTopAsync(
            new ArticleRankingQuery(windowStart, topic, limit, actorId.Trim()),
            cancellationToken).ConfigureAwait(false);
        return new ArticleDigest(
            period,
            windowStart,
            windowEnd,
            topic,
            articles);
    }
}
