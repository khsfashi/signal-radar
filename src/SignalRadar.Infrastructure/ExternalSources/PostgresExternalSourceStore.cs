using Npgsql;
using NpgsqlTypes;
using SignalRadar.Application.ExternalSources;

namespace SignalRadar.Infrastructure.ExternalSources;

public sealed class PostgresExternalSourceStore : IExternalSourceStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresExternalSourceStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async ValueTask UpsertDefinitionsAsync(
        IReadOnlyList<ExternalSourceDefinition> definitions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        const string sql = """
            INSERT INTO external_sources (
                id,
                source_type,
                source_key,
                display_name,
                endpoint_url,
                settings_json,
                enabled,
                polling_interval_seconds,
                next_poll_at)
            VALUES ($1, $2, $3, $4, $5, $6::jsonb, $7, $8, clock_timestamp())
            ON CONFLICT (source_type, source_key) DO UPDATE
            SET display_name = EXCLUDED.display_name,
                endpoint_url = EXCLUDED.endpoint_url,
                settings_json = EXCLUDED.settings_json,
                enabled = EXCLUDED.enabled,
                polling_interval_seconds = EXCLUDED.polling_interval_seconds,
                next_poll_at = CASE
                    WHEN NOT external_sources.enabled AND EXCLUDED.enabled
                        THEN LEAST(external_sources.next_poll_at, clock_timestamp())
                    ELSE external_sources.next_poll_at
                END,
                lease_token = CASE
                    WHEN EXCLUDED.enabled THEN external_sources.lease_token
                    ELSE NULL
                END,
                lease_expires_at = CASE
                    WHEN EXCLUDED.enabled THEN external_sources.lease_expires_at
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
            ExternalSourceDefinition definition = definitions[index];
            await using NpgsqlCommand command = new(sql, connection, transaction);
            command.Parameters.AddWithValue(Guid.NewGuid());
            command.Parameters.AddWithValue(definition.SourceType);
            command.Parameters.AddWithValue(definition.SourceKey);
            command.Parameters.AddWithValue(definition.DisplayName);
            command.Parameters.AddWithValue(definition.Endpoint.AbsoluteUri);
            command.Parameters.AddWithValue(definition.SettingsJson);
            command.Parameters.AddWithValue(definition.Enabled);
            command.Parameters.AddWithValue(
                checked((int)definition.PollingInterval.TotalSeconds));
            await command
                .ExecuteNonQueryAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<ExternalSourceLease>> ClaimDueAsync(
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
                FROM external_sources
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
            UPDATE external_sources AS source
            SET lease_token = $2,
                lease_expires_at = clock_timestamp()
                    + make_interval(secs => $3),
                updated_at = clock_timestamp()
            FROM candidates
            WHERE source.id = candidates.id
            RETURNING source.id,
                source.source_type,
                source.source_key,
                source.display_name,
                source.endpoint_url,
                source.polling_interval_seconds,
                source.settings_json::text,
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
        List<ExternalSourceLease> leases = new(maxCount);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string endpoint = reader.GetString(4);

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? endpointUri))
            {
                throw new InvalidDataException(
                    $"Stored external endpoint '{endpoint}' is invalid.");
            }

            leases.Add(new ExternalSourceLease(
                reader.GetGuid(0),
                leaseToken,
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                endpointUri,
                TimeSpan.FromSeconds(reader.GetInt32(5)),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetInt32(9)));
        }

        return leases;
    }

    public async ValueTask CompleteSuccessAsync(
        ExternalSourceLease source,
        string? etag,
        DateTimeOffset? lastModified,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        const string sql = """
            UPDATE external_sources
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
        ExternalSourceLease source,
        string error,
        DateTimeOffset failedAt,
        DateTimeOffset nextAttemptAt,
        DateTimeOffset? quarantineUntil,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        const string sql = """
            UPDATE external_sources
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

    private static void AddNullableText(NpgsqlCommand command, string? value)
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
                ? (object)value.Value
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
                "The external source lease was lost before completion.");
        }
    }
}
