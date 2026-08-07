namespace SignalRadar.Application.Discord;

public sealed record DiscordMessageReceiptLease(
    ulong MessageId,
    Guid Token);

public interface IDiscordMessageReceiptStore
{
    public ValueTask<DiscordMessageReceiptLease?> TryBeginAsync(
        ulong messageId,
        CancellationToken cancellationToken);

    public ValueTask CompleteAsync(
        DiscordMessageReceiptLease lease,
        CancellationToken cancellationToken);

    public ValueTask AbandonAsync(
        DiscordMessageReceiptLease lease,
        CancellationToken cancellationToken);
}
