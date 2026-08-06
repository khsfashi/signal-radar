using Npgsql;
using SignalRadar.Application.Digests;

namespace SignalRadar.Infrastructure.Digests;

public sealed class PostgresDigestDeliveryReceiptStore
    : IDigestDeliveryReceiptStore
{
    private const string BeginSql = """
        INSERT INTO digest_delivery_receipts (
            delivery_key,
            window_start,
            lease_token,
            lease_expires_at,
            delivered_at,
            discord_message_id,
            last_error)
        VALUES ($1, $2, $3, $4, NULL, NULL, NULL)
        ON CONFLICT (delivery_key, window_start) DO UPDATE
        SET lease_token = EXCLUDED.lease_token,
            lease_expires_at = EXCLUDED.lease_expires_at,
            last_error = NULL
        WHERE digest_delivery_receipts.delivered_at IS NULL
          AND digest_delivery_receipts.lease_expires_at <= $5
        RETURNING lease_token;
        """;

    private const string CompleteSql = """
        UPDATE digest_delivery_receipts
        SET delivered_at = $4,
            lease_expires_at = $4,
            discord_message_id = $5,
            last_error = NULL
        WHERE delivery_key = $1
          AND window_start = $2
          AND lease_token = $3
          AND delivered_at IS NULL;
        """;

    private const string FailSql = """
        UPDATE digest_delivery_receipts
        SET lease_expires_at = $4,
            last_error = $5
        WHERE delivery_key = $1
          AND window_start = $2
          AND lease_token = $3
          AND delivered_at IS NULL;
        """;

    private readonly NpgsqlDataSource _dataSource;

    public PostgresDigestDeliveryReceiptStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async ValueTask<DigestDeliveryLease?> TryBeginAsync(
        string deliveryKey,
        DateTimeOffset windowStart,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        string normalizedKey = NormalizeKey(deliveryKey);

        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        Guid token = Guid.NewGuid();
        await using NpgsqlCommand command = _dataSource.CreateCommand(BeginSql);
        command.Parameters.AddWithValue(normalizedKey);
        command.Parameters.AddWithValue(windowStart.ToUniversalTime());
        command.Parameters.AddWithValue(token);
        command.Parameters.AddWithValue(now.ToUniversalTime().Add(leaseDuration));
        command.Parameters.AddWithValue(now.ToUniversalTime());
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new DigestDeliveryLease(
            normalizedKey,
            windowStart.ToUniversalTime(),
            reader.GetGuid(0));
    }

    public async ValueTask CompleteAsync(
        DigestDeliveryLease lease,
        DateTimeOffset deliveredAt,
        ulong? discordMessageId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        await using NpgsqlCommand command = _dataSource.CreateCommand(CompleteSql);
        command.Parameters.AddWithValue(lease.DeliveryKey);
        command.Parameters.AddWithValue(lease.WindowStart.ToUniversalTime());
        command.Parameters.AddWithValue(lease.LeaseToken);
        command.Parameters.AddWithValue(deliveredAt.ToUniversalTime());
        command.Parameters.AddWithValue(
            discordMessageId.HasValue
                ? (object)(decimal)discordMessageId.Value
                : DBNull.Value);
        int affectedRows = await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);

        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "The digest delivery lease is no longer owned by this worker.");
        }
    }

    public async ValueTask FailAsync(
        DigestDeliveryLease lease,
        DateTimeOffset failedAt,
        string error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        string normalizedError = string.IsNullOrWhiteSpace(error)
            ? "Unknown scheduled digest failure."
            : error.Replace('\r', ' ').Replace('\n', ' ').Trim();

        if (normalizedError.Length > 1000)
        {
            normalizedError = normalizedError[..1000];
        }

        await using NpgsqlCommand command = _dataSource.CreateCommand(FailSql);
        command.Parameters.AddWithValue(lease.DeliveryKey);
        command.Parameters.AddWithValue(lease.WindowStart.ToUniversalTime());
        command.Parameters.AddWithValue(lease.LeaseToken);
        command.Parameters.AddWithValue(failedAt.ToUniversalTime());
        command.Parameters.AddWithValue(normalizedError);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeKey(string deliveryKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryKey);
        string normalized = deliveryKey.Trim();

        return normalized.Length <= 200
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(deliveryKey));
    }
}
