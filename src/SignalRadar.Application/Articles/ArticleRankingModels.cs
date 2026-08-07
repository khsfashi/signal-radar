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

public interface IArticleSaveStore
{
    public ValueTask<bool> TryAddAsync(
        Guid articleId,
        string actorId,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken);

    public ValueTask<bool> RemoveAsync(
        Guid articleId,
        string actorId,
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

public sealed record SavedArticleQuery(
    DateTimeOffset SavedSince,
    ArticleTopic TopicMask,
    int Limit,
    string ActorId);

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

public sealed record SavedArticle(
    Guid ArticleId,
    string Title,
    Uri CanonicalUrl,
    string Source,
    DateTimeOffset PublishedAt,
    DateTimeOffset SavedAt,
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

public interface IArticleSavedReader
{
    public ValueTask<IReadOnlyList<SavedArticle>> GetSavedAsync(
        SavedArticleQuery query,
        CancellationToken cancellationToken);
}
