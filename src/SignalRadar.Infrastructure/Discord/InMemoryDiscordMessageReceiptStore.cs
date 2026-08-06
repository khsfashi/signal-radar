using SignalRadar.Application.Discord;

namespace SignalRadar.Infrastructure.Discord;

public sealed class InMemoryDiscordMessageReceiptStore : IDiscordMessageReceiptStore
{
    private readonly object _gate = new();
    private readonly Dictionary<ulong, ReceiptEntry> _entries = new();

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public ValueTask<DiscordMessageReceiptLease?> TryBeginAsync(
        ulong messageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_entries.ContainsKey(messageId))
            {
                return ValueTask.FromResult<DiscordMessageReceiptLease?>(null);
            }

            Guid token = Guid.NewGuid();
            _entries.Add(messageId, new ReceiptEntry(token, Completed: false));

            return ValueTask.FromResult<DiscordMessageReceiptLease?>(
                new DiscordMessageReceiptLease(messageId, token));
        }
    }

    public ValueTask CompleteAsync(
        DiscordMessageReceiptLease lease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_entries.TryGetValue(lease.MessageId, out ReceiptEntry? entry)
                && entry.Token == lease.Token
                && !entry.Completed)
            {
                _entries[lease.MessageId] = entry with { Completed = true };
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask AbandonAsync(
        DiscordMessageReceiptLease lease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_entries.TryGetValue(lease.MessageId, out ReceiptEntry? entry)
                && entry.Token == lease.Token
                && !entry.Completed)
            {
                _entries.Remove(lease.MessageId);
            }
        }

        return ValueTask.CompletedTask;
    }

    private sealed record ReceiptEntry(
        Guid Token,
        bool Completed);
}
