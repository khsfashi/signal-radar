using Npgsql;
using SignalRadar.Application.Articles;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Infrastructure.Ranking;

public sealed class PostgresArticleRankingReader : IArticleRankingReader
{
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

        if (query.Limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(query));
        }

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
            WHERE article.published_at >= $1
                AND ($2 = 0 OR (article.topics & $2) <> 0)
            ORDER BY effective_score DESC,
                article.published_at DESC,
                article.id
            LIMIT $3;
            """;

        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(query.Since.ToUniversalTime());
        command.Parameters.AddWithValue((int)query.TopicMask);
        command.Parameters.AddWithValue(query.Limit);
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        List<RankedArticle> articles = new(query.Limit);

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
}
