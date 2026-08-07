namespace SignalRadar.Application.Feeds;

public sealed record ManagedFeedSource(
    string Name,
    Uri FeedUrl,
    TimeSpan PollingInterval,
    bool Enabled,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    string? LastError);

public interface IFeedSourceAdministrationStore
{
    public ValueTask<IReadOnlyList<ManagedFeedSource>> GetAllAsync(
        CancellationToken cancellationToken);

    public ValueTask UpsertAsync(
        FeedSourceDefinition definition,
        CancellationToken cancellationToken);

    public ValueTask<bool> SetEnabledAsync(
        string name,
        bool enabled,
        CancellationToken cancellationToken);
}
