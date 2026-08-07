using Npgsql;
using SignalRadar.Application.Articles;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Infrastructure.Ranking;

public sealed class PostgresArticleSaveStore : IArticleSaveStore, IArticleSavedReader
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresArticleSaveStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async ValueTask<bool> TryAddAsync(
        Guid articleId,
        string actorId,
        DateTimeOffset savedAt,
        CancellationToken cancellationToken)
    {
        ValidateArticleId(articleId);
        string normalizedActorId = NormalizeActorId(actorId);
        const string sql = """
            INSERT INTO article_saves (
                article_id,
                actor_id,
                saved_at)
            VALUES ($1, $2, $3)
            ON CONFLICT (article_id, actor_id) DO NOTHING;
            """;

        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(articleId);
        command.Parameters.AddWithValue(normalizedActorId);
        command.Parameters.AddWithValue(savedAt.ToUniversalTime());
        int affectedRows = await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        return affectedRows == 1;
    }

    public async ValueTask<bool> RemoveAsync(
        Guid articleId,
        string actorId,
        CancellationToken cancellationToken)
    {
        ValidateArticleId(articleId);
        string normalizedActorId = NormalizeActorId(actorId);
        const string sql = """
            DELETE FROM article_saves
            WHERE article_id = $1
                AND actor_id = $2;
            """;

        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(articleId);
        command.Parameters.AddWithValue(normalizedActorId);
        int affectedRows = await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
        return affectedRows == 1;
    }

    public async ValueTask<IReadOnlyList<SavedArticle>> GetSavedAsync(
        SavedArticleQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(query));
        }

        string normalizedActorId = NormalizeActorId(query.ActorId);
        const string sql = """
            WITH feedback AS (
                SELECT article_id,
                    COALESCE(SUM(weight), 0)::integer AS total_weight
                FROM article_feedback
                GROUP BY article_id
            )
            SELECT article.id,
                article.title,
                article.canonical_url,
                article.source,
                article.published_at,
                saved.saved_at,
                article.topics,
                article.primary_topic,
                article.base_score,
                COALESCE(feedback.total_weight, 0) AS feedback_weight,
                GREATEST(
                    0,
                    LEAST(
                        100,
                        article.base_score
                            + COALESCE(feedback.total_weight, 0) * 5))::numeric(5, 2)
                    AS effective_score
            FROM article_saves AS saved
            INNER JOIN articles AS article ON article.id = saved.article_id
            LEFT JOIN feedback ON feedback.article_id = article.id
            WHERE saved.actor_id = $1
                AND saved.saved_at >= $2
                AND ($3 = 0 OR (article.topics & $3) <> 0)
            ORDER BY saved.saved_at DESC,
                effective_score DESC,
                article.id
            LIMIT $4;
            """;

        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(normalizedActorId);
        command.Parameters.AddWithValue(query.SavedSince.ToUniversalTime());
        command.Parameters.AddWithValue((int)query.TopicMask);
        command.Parameters.AddWithValue(query.Limit);
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        List<SavedArticle> articles = new(query.Limit);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string canonicalUrl = reader.GetString(2);

            if (!Uri.TryCreate(canonicalUrl, UriKind.Absolute, out Uri? canonicalUri))
            {
                throw new InvalidDataException(
                    $"Stored canonical URL '{canonicalUrl}' is invalid.");
            }

            articles.Add(new SavedArticle(
                reader.GetGuid(0),
                reader.GetString(1),
                canonicalUri,
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                (ArticleTopic)reader.GetInt32(6),
                (ArticleTopic)reader.GetInt16(7),
                reader.GetDecimal(8),
                reader.GetInt32(9),
                reader.GetDecimal(10)));
        }

        return articles;
    }

    private static void ValidateArticleId(Guid articleId)
    {
        if (articleId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(articleId));
        }
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
