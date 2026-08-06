using SignalRadar.Domain.Articles;

namespace SignalRadar.Application.Articles;

public interface IArticleFeedbackStore
{
    public ValueTask SetAsync(
        Guid articleId,
        string actorId,
        ArticleFeedbackKind kind,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken);

    public ValueTask RemoveAsync(
        Guid articleId,
        string actorId,
        CancellationToken cancellationToken);

    public ValueTask<ArticleFeedbackAggregate> GetAggregateAsync(
        Guid articleId,
        CancellationToken cancellationToken);
}

public sealed record ArticleRankingQuery(
    DateTimeOffset Since,
    ArticleTopic TopicMask,
    int Limit,
    string? ActorId = null);

public sealed record ArticleSearchQuery(
    string Text,
    DateTimeOffset Since,
    ArticleTopic TopicMask,
    int Limit,
    string? ActorId = null);

public sealed record RankedArticle(
    Guid ArticleId,
    string Title,
    Uri CanonicalUrl,
    string Source,
    DateTimeOffset PublishedAt,
    ArticleTopic Topics,
    ArticleTopic PrimaryTopic,
    decimal BaseScore,
    int FeedbackWeight,
    decimal EffectiveScore);

public interface IArticleRankingReader
{
    public ValueTask<IReadOnlyList<RankedArticle>> GetTopAsync(
        ArticleRankingQuery query,
        CancellationToken cancellationToken);

    public ValueTask<IReadOnlyList<RankedArticle>> SearchAsync(
        ArticleSearchQuery query,
        CancellationToken cancellationToken);
}
