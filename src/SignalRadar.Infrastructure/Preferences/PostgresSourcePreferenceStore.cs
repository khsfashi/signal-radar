using System.Collections.Frozen;
using Npgsql;
using SignalRadar.Application.Preferences;

namespace SignalRadar.Infrastructure.Preferences;

public sealed class PostgresSourcePreferenceStore : ISourcePreferenceStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresSourcePreferenceStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async ValueTask<bool> MuteAsync(
        string actorId,
        string source,
        CancellationToken cancellationToken)
    {
        (string actor, string normalizedSource) = Normalize(actorId, source);
        const string sql = """
            INSERT INTO actor_muted_sources (actor_id, source)
            VALUES ($1, $2)
            ON CONFLICT (actor_id, source) DO NOTHING;
            """;
        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(actor);
        command.Parameters.AddWithValue(normalizedSource);
        return await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false) > 0;
    }

    public async ValueTask<bool> UnmuteAsync(
        string actorId,
        string source,
        CancellationToken cancellationToken)
    {
        (string actor, string normalizedSource) = Normalize(actorId, source);
        const string sql = """
            DELETE FROM actor_muted_sources
            WHERE actor_id = $1 AND source = $2;
            """;
        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(actor);
        command.Parameters.AddWithValue(normalizedSource);
        return await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false) > 0;
    }

    public async ValueTask<IReadOnlySet<string>> GetMutedSourcesAsync(
        string actorId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        string actor = actorId.Trim();
        const string sql = """
            SELECT source
            FROM actor_muted_sources
            WHERE actor_id = $1
            ORDER BY source;
            """;
        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(actor);
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        HashSet<string> sources = new(StringComparer.OrdinalIgnoreCase);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sources.Add(reader.GetString(0));
        }

        return sources.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    private static (string ActorId, string Source) Normalize(
        string actorId,
        string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        string actor = actorId.Trim();
        string normalizedSource = source.Trim();

        if (actor.Length > 200 || normalizedSource.Length > 200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(source),
                "Actor IDs and source names cannot exceed 200 characters.");
        }

        return (actor, normalizedSource);
    }
}
