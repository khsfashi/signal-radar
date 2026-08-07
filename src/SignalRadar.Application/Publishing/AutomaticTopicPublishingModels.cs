using SignalRadar.Domain.Articles;

namespace SignalRadar.Application.Publishing;

public sealed record AutomaticTopicPublicationQuery(
    ArticleTopic Topic,
    ulong ChannelId,
    DateTimeOffset CollectedAfter,
    decimal MinimumScore,
    int Limit,
    string PublicationKind);

public sealed record AutomaticTopicPublicationCandidate(
    Guid ArticleId,
    string Title,
    Uri CanonicalUrl,
    string Source,
    DateTimeOffset PublishedAt,
    DateTimeOffset CollectedAt,
    ArticleTopic Topics,
    ArticleTopic PrimaryTopic,
    decimal BaseScore,
    int FeedbackWeight,
    decimal EffectiveScore);

public sealed record AutomaticTopicPublicationLease(
    Guid ArticleId,
    ulong ChannelId,
    string PublicationKind,
    Guid LeaseToken);

public interface IAutomaticTopicPublicationStore
{
    public ValueTask<DateTimeOffset> GetOrCreateActivationTimeAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);

    public ValueTask<IReadOnlyList<AutomaticTopicPublicationCandidate>> GetCandidatesAsync(
        AutomaticTopicPublicationQuery query,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    public ValueTask<AutomaticTopicPublicationLease?> TryBeginAsync(
        Guid articleId,
        ulong channelId,
        string publicationKind,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    public ValueTask CompleteAsync(
        AutomaticTopicPublicationLease lease,
        DateTimeOffset deliveredAt,
        ulong discordResourceId,
        CancellationToken cancellationToken);

    public ValueTask FailAsync(
        AutomaticTopicPublicationLease lease,
        DateTimeOffset failedAt,
        TimeSpan retryDelay,
        string error,
        CancellationToken cancellationToken);
}
