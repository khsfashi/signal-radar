using Npgsql;
using SignalRadar.Application.Publishing;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Infrastructure.Publishing;

public sealed class PostgresDiscordTopicRouteStore : IDiscordTopicRouteStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresDiscordTopicRouteStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async ValueTask<DateTimeOffset> GetOrCreateBatchActivationTimeAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO discord_topic_batch_state (singleton, activated_at)
            VALUES (TRUE, $1)
            ON CONFLICT (singleton) DO UPDATE
            SET activated_at = discord_topic_batch_state.activated_at
            RETURNING activated_at;
            """;
        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(now.ToUniversalTime());
        object? value = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return value switch
        {
            DateTimeOffset activatedAt => activatedAt,
            DateTime activatedAt => new DateTimeOffset(
                DateTime.SpecifyKind(activatedAt, DateTimeKind.Utc)),
            _ => throw new InvalidDataException(
                "Discord topic batch activation time was not returned.")
        };
    }

    public async ValueTask<IReadOnlyList<ManagedDiscordTopicRoute>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT topic, channel_id, minimum_score, batch_window_seconds, enabled
            FROM discord_topic_routes
            ORDER BY topic;
            """;
        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<ManagedDiscordTopicRoute> routes = [];

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            decimal channelValue = reader.GetDecimal(1);

            if (channelValue <= 0 || channelValue > ulong.MaxValue)
            {
                throw new InvalidDataException("Stored Discord channel ID is invalid.");
            }

            routes.Add(new ManagedDiscordTopicRoute(
                (ArticleTopic)reader.GetInt32(0),
                decimal.ToUInt64(channelValue),
                reader.GetDecimal(2),
                TimeSpan.FromSeconds(reader.GetInt32(3)),
                reader.GetBoolean(4)));
        }

        return routes;
    }

    public async ValueTask UpsertAsync(
        ManagedDiscordTopicRoute route,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(route);
        Validate(route);
        const string sql = """
            INSERT INTO discord_topic_routes (
                topic,
                channel_id,
                minimum_score,
                batch_window_seconds,
                enabled)
            VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (topic) DO UPDATE
            SET channel_id = EXCLUDED.channel_id,
                minimum_score = EXCLUDED.minimum_score,
                batch_window_seconds = EXCLUDED.batch_window_seconds,
                enabled = EXCLUDED.enabled,
                updated_at = clock_timestamp();
            """;
        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue((int)route.Topic);
        command.Parameters.AddWithValue((decimal)route.ChannelId);
        command.Parameters.AddWithValue(route.MinimumScore);
        command.Parameters.AddWithValue(checked((int)route.BatchWindow.TotalSeconds));
        command.Parameters.AddWithValue(route.Enabled);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> RemoveAsync(
        ArticleTopic topic,
        CancellationToken cancellationToken)
    {
        ValidateTopic(topic);
        const string sql = "DELETE FROM discord_topic_routes WHERE topic = $1;";
        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue((int)topic);
        return await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false) > 0;
    }

    private static void Validate(ManagedDiscordTopicRoute route)
    {
        ValidateTopic(route.Topic);

        if (route.ChannelId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(route));
        }

        if (route.MinimumScore is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(route));
        }

        if (route.BatchWindow < TimeSpan.FromMinutes(5)
            || route.BatchWindow > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(route));
        }
    }

    private static void ValidateTopic(ArticleTopic topic)
    {
        int value = (int)topic;

        if (value <= 0 || (value & (value - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topic));
        }
    }
}
