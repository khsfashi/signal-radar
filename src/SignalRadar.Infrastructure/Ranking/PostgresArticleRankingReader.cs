using Npgsql;
using NpgsqlTypes;
using SignalRadar.Application.Articles;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Infrastructure.Ranking;

public sealed class PostgresArticleRankingReader : IArticleRankingReader
{
    private const short HiddenFeedbackKind = (short)ArticleFeedbackKind.Hidden;
    private readonly NpgsqlDataSource _dataSource;

    public PostgresArticleRankingReader(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async ValueTask<IReadOnlyList<RankedArticle>> GetTopAsync(
        ArticleRankingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateLimit(query.Limit, maximum: 100);
        string? actorId = NormalizeActorId(query.ActorId);

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
            FROM articles AS article
            LEFT JOIN feedback ON feedback.article_id = article.id
            LEFT JOIN article_feedback AS actor_feedback
                ON actor_feedback.article_id = article.id
                AND actor_feedback.actor_id = $4
            WHERE article.published_at >= $1
                AND ($2 = 0 OR (article.topics & $2) <> 0)
                AND ($4::text IS NULL OR actor_feedback.kind IS DISTINCT FROM $5)
            ORDER BY effective_score DESC,
                article.published_at DESC,
                article.id
            LIMIT $3;
            """;

        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(query.Since.ToUniversalTime());
        command.Parameters.AddWithValue((int)query.TopicMask);
        command.Parameters.AddWithValue(query.Limit);
        AddNullableActorId(command, actorId);
        command.Parameters.AddWithValue(HiddenFeedbackKind);
        return await ReadArticlesAsync(command, query.Limit, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<RankedArticle>> SearchAsync(
        ArticleSearchQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        string searchText = NormalizeSearchText(query.Text);
        ValidateLimit(query.Limit, maximum: 25);
        string? actorId = NormalizeActorId(query.ActorId);

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
            FROM articles AS article
            LEFT JOIN feedback ON feedback.article_id = article.id
            LEFT JOIN article_feedback AS actor_feedback
                ON actor_feedback.article_id = article.id
                AND actor_feedback.actor_id = $5
            WHERE article.published_at >= $2
                AND ($3 = 0 OR (article.topics & $3) <> 0)
                AND (
                    strpos(lower(article.title), lower($1)) > 0
                    OR strpos(lower(article.source), lower($1)) > 0)
                AND ($5::text IS NULL OR actor_feedback.kind IS DISTINCT FROM $6)
            ORDER BY
                CASE
                    WHEN lower(article.title) = lower($1) THEN 0
                    WHEN strpos(lower(article.title), lower($1)) = 1 THEN 1
                    ELSE 2
                END,
                effective_score DESC,
                article.published_at DESC,
                article.id
            LIMIT $4;
            """;

        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(searchText);
        command.Parameters.AddWithValue(query.Since.ToUniversalTime());
        command.Parameters.AddWithValue((int)query.TopicMask);
        command.Parameters.AddWithValue(query.Limit);
        AddNullableActorId(command, actorId);
        command.Parameters.AddWithValue(HiddenFeedbackKind);
        return await ReadArticlesAsync(command, query.Limit, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask<IReadOnlyList<RankedArticle>> ReadArticlesAsync(
        NpgsqlCommand command,
        int capacity,
        CancellationToken cancellationToken)
    {
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        List<RankedArticle> articles = new(capacity);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string canonicalUrl = reader.GetString(2);

            if (!Uri.TryCreate(
                canonicalUrl,
                UriKind.Absolute,
                out Uri? canonicalUri))
            {
                throw new InvalidDataException(
                    $"Stored canonical URL '{canonicalUrl}' is invalid.");
            }

            articles.Add(new RankedArticle(
                reader.GetGuid(0),
                reader.GetString(1),
                canonicalUri,
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                (ArticleTopic)reader.GetInt32(5),
                (ArticleTopic)reader.GetInt16(6),
                reader.GetDecimal(7),
                reader.GetInt32(8),
                reader.GetDecimal(9)));
        }

        return articles;
    }

    private static void AddNullableActorId(
        NpgsqlCommand command,
        string? actorId)
    {
        NpgsqlParameter parameter = new()
        {
            NpgsqlDbType = NpgsqlDbType.Text,
            Value = (object?)actorId ?? DBNull.Value
        };
        command.Parameters.Add(parameter);
    }

    private static string NormalizeSearchText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        string normalized = text.Trim();

        if (normalized.Length is < 2 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(text),
                "Search text must contain between 2 and 100 characters.");
        }

        return normalized;
    }

    private static string? NormalizeActorId(string? actorId)
    {
        if (actorId is null)
        {
            return null;
        }

        string normalized = actorId.Trim();

        if (normalized.Length is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(actorId));
        }

        return normalized;
    }

    private static void ValidateLimit(int limit, int maximum)
    {
        if (limit < 1 || limit > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }
    }
}
