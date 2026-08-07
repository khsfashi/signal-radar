using Npgsql;
using SignalRadar.Application.Discord;

namespace SignalRadar.Infrastructure.Discord;

public sealed class PostgresDiscordMessageReceiptStore : IDiscordMessageReceiptStore
{
    private const string BeginSql = """
        INSERT INTO discord_message_receipts (
            message_id,
            lease_token,
            lease_expires_at,
            completed_at)
        VALUES ($1, $2, clock_timestamp() + $3, NULL)
        ON CONFLICT (message_id) DO UPDATE
        SET lease_token = EXCLUDED.lease_token,
            lease_expires_at = EXCLUDED.lease_expires_at
        WHERE discord_message_receipts.completed_at IS NULL
          AND discord_message_receipts.lease_expires_at <= clock_timestamp()
        RETURNING lease_token;
        """;

    private const string CompleteSql = """
        UPDATE discord_message_receipts
        SET completed_at = clock_timestamp(),
            lease_expires_at = clock_timestamp()
        WHERE message_id = $1
          AND lease_token = $2
          AND completed_at IS NULL;
        """;

    private const string AbandonSql = """
        DELETE FROM discord_message_receipts
        WHERE message_id = $1
          AND lease_token = $2
          AND completed_at IS NULL;
        """;

    private readonly NpgsqlDataSource _dataSource;
    private readonly TimeSpan _leaseDuration;

    public PostgresDiscordMessageReceiptStore(
        NpgsqlDataSource dataSource,
        TimeSpan leaseDuration)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));

        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration),
                "The lease duration must be positive.");
        }

        _leaseDuration = leaseDuration;
    }

    public async ValueTask<DiscordMessageReceiptLease?> TryBeginAsync(
        ulong messageId,
        CancellationToken cancellationToken)
    {
        Guid token = Guid.NewGuid();

        await using NpgsqlCommand command = _dataSource.CreateCommand(BeginSql);
        command.Parameters.AddWithValue((decimal)messageId);
        command.Parameters.AddWithValue(token);
        command.Parameters.AddWithValue(_leaseDuration);

        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new DiscordMessageReceiptLease(messageId, reader.GetGuid(0));
    }

    public async ValueTask CompleteAsync(
        DiscordMessageReceiptLease lease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);

        await using NpgsqlCommand command = _dataSource.CreateCommand(CompleteSql);
        command.Parameters.AddWithValue((decimal)lease.MessageId);
        command.Parameters.AddWithValue(lease.Token);

        int affectedRows = await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);

        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "The Discord message receipt lease is no longer owned by this worker.");
        }
    }

    public async ValueTask AbandonAsync(
        DiscordMessageReceiptLease lease,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);

        await using NpgsqlCommand command = _dataSource.CreateCommand(AbandonSql);
        command.Parameters.AddWithValue((decimal)lease.MessageId);
        command.Parameters.AddWithValue(lease.Token);

        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
