namespace SignalRadar.Application.Preferences;

public interface ISourcePreferenceStore
{
    public ValueTask<bool> MuteAsync(
        string actorId,
        string source,
        CancellationToken cancellationToken);

    public ValueTask<bool> UnmuteAsync(
        string actorId,
        string source,
        CancellationToken cancellationToken);

    public ValueTask<IReadOnlySet<string>> GetMutedSourcesAsync(
        string actorId,
        CancellationToken cancellationToken);
}
