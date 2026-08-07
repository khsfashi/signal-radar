using Npgsql;
using SignalRadar.Application.Feeds;

namespace SignalRadar.Infrastructure.Feeds;

public sealed class PostgresFeedSourceAdministrationStore
    : IFeedSourceAdministrationStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresFeedSourceAdministrationStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async ValueTask<IReadOnlyList<ManagedFeedSource>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT name,
                feed_url,
                polling_interval_seconds,
                enabled,
                last_success_at,
                last_failure_at,
                last_error
            FROM feed_sources
            ORDER BY name, feed_url;
            """;
        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<ManagedFeedSource> sources = [];

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string url = reader.GetString(1);

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? feedUrl))
            {
                throw new InvalidDataException($"Stored feed URL '{url}' is invalid.");
            }

            sources.Add(new ManagedFeedSource(
                reader.GetString(0),
                feedUrl,
                TimeSpan.FromSeconds(reader.GetInt32(2)),
                reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return sources;
    }

    public async ValueTask UpsertAsync(
        FeedSourceDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
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
                        THEN clock_timestamp()
                    ELSE LEAST(feed_sources.next_poll_at, clock_timestamp())
                END,
                quarantine_until = CASE
                    WHEN EXCLUDED.enabled THEN NULL
                    ELSE feed_sources.quarantine_until
                END,
                updated_at = clock_timestamp();
            """;
        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(definition.Name);
        command.Parameters.AddWithValue(definition.FeedUrl.AbsoluteUri);
        command.Parameters.AddWithValue(definition.Enabled);
        command.Parameters.AddWithValue(
            checked((int)definition.PollingInterval.TotalSeconds));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> SetEnabledAsync(
        string name,
        bool enabled,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        const string sql = """
            UPDATE feed_sources
            SET enabled = $2,
                next_poll_at = CASE WHEN $2 THEN clock_timestamp() ELSE next_poll_at END,
                lease_token = CASE WHEN $2 THEN lease_token ELSE NULL END,
                lease_expires_at = CASE WHEN $2 THEN lease_expires_at ELSE NULL END,
                quarantine_until = CASE WHEN $2 THEN NULL ELSE quarantine_until END,
                updated_at = clock_timestamp()
            WHERE lower(name) = lower($1);
            """;
        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(name.Trim());
        command.Parameters.AddWithValue(enabled);
        return await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false) > 0;
    }
}
