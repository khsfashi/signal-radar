using SignalRadar.Domain.Articles;

namespace SignalRadar.Application.Publishing;

public sealed record ManagedDiscordTopicRoute(
    ArticleTopic Topic,
    ulong ChannelId,
    decimal MinimumScore,
    TimeSpan BatchWindow,
    bool Enabled = true);

public interface IDiscordTopicRouteStore
{
    public ValueTask<IReadOnlyList<ManagedDiscordTopicRoute>> GetAllAsync(
        CancellationToken cancellationToken);

    public ValueTask UpsertAsync(
        ManagedDiscordTopicRoute route,
        CancellationToken cancellationToken);

    public ValueTask<bool> RemoveAsync(
        ArticleTopic topic,
        CancellationToken cancellationToken);
}
