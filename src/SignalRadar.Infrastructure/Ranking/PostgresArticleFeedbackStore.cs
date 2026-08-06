using Npgsql;
using SignalRadar.Application.Articles;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Infrastructure.Ranking;

public sealed class PostgresArticleFeedbackStore : IArticleFeedbackStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresArticleFeedbackStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async ValueTask SetAsync(
        Guid articleId,
        string actorId,
        ArticleFeedbackKind kind,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        if (articleId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(articleId));
        }

        string normalizedActorId = NormalizeActorId(actorId);
        int weight = ArticleFeedbackWeights.GetWeight(kind);
        const string sql = """
            INSERT INTO article_feedback (
                article_id,
                actor_id,
                kind,
                weight,
                occurred_at)
            VALUES ($1, $2, $3, $4, $5)
            ON CONFLICT (article_id, actor_id) DO UPDATE
            SET kind = EXCLUDED.kind,
                weight = EXCLUDED.weight,
                occurred_at = EXCLUDED.occurred_at,
                updated_at = clock_timestamp();
            """;

        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(articleId);
        command.Parameters.AddWithValue(normalizedActorId);
        command.Parameters.AddWithValue((short)kind);
        command.Parameters.AddWithValue((short)weight);
        command.Parameters.AddWithValue(occurredAt.ToUniversalTime());
        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask RemoveAsync(
        Guid articleId,
        string actorId,
        CancellationToken cancellationToken)
    {
        if (articleId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(articleId));
        }

        string normalizedActorId = NormalizeActorId(actorId);
        const string sql = """
            DELETE FROM article_feedback
            WHERE article_id = $1
                AND actor_id = $2;
            """;

        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(articleId);
        command.Parameters.AddWithValue(normalizedActorId);
        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<ArticleFeedbackAggregate> GetAggregateAsync(
        Guid articleId,
        CancellationToken cancellationToken)
    {
        if (articleId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(articleId));
        }

        const string sql = """
            SELECT COUNT(*)::integer,
                COALESCE(SUM(weight), 0)::integer
            FROM article_feedback
            WHERE article_id = $1;
            """;

        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(articleId);
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                "The feedback aggregate query returned no row.");
        }

        return new ArticleFeedbackAggregate(
            reader.GetInt32(0),
            reader.GetInt32(1));
    }

    private static string NormalizeActorId(string actorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        string normalized = actorId.Trim();

        if (normalized.Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(actorId));
        }

        return normalized;
    }
}
