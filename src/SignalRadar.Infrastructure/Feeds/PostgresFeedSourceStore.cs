using Npgsql;
using NpgsqlTypes;
using SignalRadar.Application.Feeds;

namespace SignalRadar.Infrastructure.Feeds;

public sealed class PostgresFeedSourceStore : IFeedSourceStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresFeedSourceStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async ValueTask UpsertDefinitionsAsync(
        IReadOnlyList<FeedSourceDefinition> definitions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        const string sql = """
            INSERT INTO feed_sources (
                id,
                name,
                feed_url,
                enabled,
                polling_interval_seconds,
                next_poll_at)
            VALUES ($1, $2, $3, $4, $5, clock_timestamp())
            ON CONFLICT (feed_url) DO UPDATE
            SET name = EXCLUDED.name,
                enabled = EXCLUDED.enabled,
                polling_interval_seconds = EXCLUDED.polling_interval_seconds,
                next_poll_at = CASE
                    WHEN NOT feed_sources.enabled AND EXCLUDED.enabled
                        THEN LEAST(feed_sources.next_poll_at, clock_timestamp())
                    ELSE feed_sources.next_poll_at
                END,
                lease_token = CASE
                    WHEN EXCLUDED.enabled THEN feed_sources.lease_token
                    ELSE NULL
                END,
                lease_expires_at = CASE
                    WHEN EXCLUDED.enabled THEN feed_sources.lease_expires_at
                    ELSE NULL
                END,
                updated_at = clock_timestamp();
            """;

        await using NpgsqlConnection connection = await _dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        for (int index = 0; index < definitions.Count; index++)
        {
            FeedSourceDefinition definition = definitions[index];
            await using NpgsqlCommand command = new(sql, connection, transaction);
            command.Parameters.AddWithValue(Guid.NewGuid());
            command.Parameters.AddWithValue(definition.Name);
            command.Parameters.AddWithValue(definition.FeedUrl.AbsoluteUri);
            command.Parameters.AddWithValue(definition.Enabled);
            command.Parameters.AddWithValue(
                checked((int)definition.PollingInterval.TotalSeconds));
            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<FeedSourceLease>> ClaimDueAsync(
        int maxCount,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        if (maxCount is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount));
        }

        if (leaseDuration < TimeSpan.FromSeconds(30)
            || leaseDuration > TimeSpan.FromMinutes(30))
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        const string sql = """
            WITH candidates AS (
                SELECT id
                FROM feed_sources
                WHERE enabled
                    AND next_poll_at <= clock_timestamp()
                    AND (quarantine_until IS NULL
                        OR quarantine_until <= clock_timestamp())
                    AND (lease_expires_at IS NULL
                        OR lease_expires_at <= clock_timestamp())
                ORDER BY next_poll_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT $1
            )
            UPDATE feed_sources AS source
            SET lease_token = $2,
                lease_expires_at = clock_timestamp()
                    + make_interval(secs => $3),
                updated_at = clock_timestamp()
            FROM candidates
            WHERE source.id = candidates.id
            RETURNING source.id,
                source.name,
                source.feed_url,
                source.polling_interval_seconds,
                source.etag,
                source.last_modified,
                source.consecutive_failures;
            """;

        Guid leaseToken = Guid.NewGuid();
        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(maxCount);
        command.Parameters.AddWithValue(leaseToken);
        command.Parameters.AddWithValue(checked((int)leaseDuration.TotalSeconds));
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        List<FeedSourceLease> leases = new(maxCount);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string feedUrl = reader.GetString(2);

            if (!Uri.TryCreate(feedUrl, UriKind.Absolute, out Uri? uri))
            {
                throw new InvalidDataException(
                    $"Stored feed URL '{feedUrl}' is invalid.");
            }

            leases.Add(new FeedSourceLease(
                reader.GetGuid(0),
                leaseToken,
                reader.GetString(1),
                uri,
                TimeSpan.FromSeconds(reader.GetInt32(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetInt32(6)));
        }

        return leases;
    }

    public async ValueTask CompleteSuccessAsync(
        FeedSourceLease source,
        string? etag,
        DateTimeOffset? lastModified,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        const string sql = """
            UPDATE feed_sources
            SET etag = $3,
                last_modified = $4,
                next_poll_at = $5,
                consecutive_failures = 0,
                last_success_at = $6,
                last_error = NULL,
                quarantine_until = NULL,
                lease_token = NULL,
                lease_expires_at = NULL,
                updated_at = clock_timestamp()
            WHERE id = $1
                AND lease_token = $2;
            """;

        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(source.SourceId);
        command.Parameters.AddWithValue(source.LeaseToken);
        AddNullableText(command, etag);
        AddNullableTimestamp(command, lastModified);
        command.Parameters.AddWithValue(completedAt.Add(source.PollingInterval));
        command.Parameters.AddWithValue(completedAt);
        await EnsureSingleUpdateAsync(command, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask CompleteFailureAsync(
        FeedSourceLease source,
        string error,
        DateTimeOffset failedAt,
        DateTimeOffset nextAttemptAt,
        DateTimeOffset? quarantineUntil,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        const string sql = """
            UPDATE feed_sources
            SET next_poll_at = $3,
                consecutive_failures = $4,
                last_failure_at = $5,
                last_error = $6,
                quarantine_until = $7,
                lease_token = NULL,
                lease_expires_at = NULL,
                updated_at = clock_timestamp()
            WHERE id = $1
                AND lease_token = $2;
            """;

        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(source.SourceId);
        command.Parameters.AddWithValue(source.LeaseToken);
        command.Parameters.AddWithValue(nextAttemptAt);
        command.Parameters.AddWithValue(source.ConsecutiveFailures + 1);
        command.Parameters.AddWithValue(failedAt);
        command.Parameters.AddWithValue(error.Length <= 2000 ? error : error[..2000]);
        AddNullableTimestamp(command, quarantineUntil);
        await EnsureSingleUpdateAsync(command, cancellationToken).ConfigureAwait(false);
    }

    private static void AddNullableText(
        NpgsqlCommand command,
        string? value)
    {
        NpgsqlParameter parameter = new()
        {
            NpgsqlDbType = NpgsqlDbType.Text,
            Value = (object?)value ?? DBNull.Value
        };
        command.Parameters.Add(parameter);
    }

    private static void AddNullableTimestamp(
        NpgsqlCommand command,
        DateTimeOffset? value)
    {
        NpgsqlParameter parameter = new()
        {
            NpgsqlDbType = NpgsqlDbType.TimestampTz,
            Value = value.HasValue
                ? (object)value.Value.ToUniversalTime()
                : DBNull.Value
        };
        command.Parameters.Add(parameter);
    }

    private static async ValueTask EnsureSingleUpdateAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        int affectedRows = await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);

        if (affectedRows != 1)
        {
            throw new InvalidOperationException(
                "The feed source lease was lost before completion.");
        }
    }
}
