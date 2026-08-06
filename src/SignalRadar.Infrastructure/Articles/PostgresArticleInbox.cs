using Npgsql;
using NpgsqlTypes;
using SignalRadar.Application.Articles;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Infrastructure.Articles;

public sealed class PostgresArticleInbox : IArticleInbox
{
    private const string InsertSql = """
        INSERT INTO articles (
            id,
            canonical_url,
            title,
            source,
            external_id,
            published_at,
            collected_at,
            topics,
            primary_topic,
            source_trust,
            topic_interest,
            practical_impact,
            freshness,
            base_score,
            ranking_profile_version)
        VALUES (
            $1, $2, $3, $4, $5, $6, $7, $8, $9, $10,
            $11, $12, $13, $14, $15)
        ON CONFLICT DO NOTHING;
        """;

    private readonly NpgsqlDataSource _dataSource;

    public PostgresArticleInbox(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async ValueTask<bool> TryAddAsync(
        Article article,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(article);

        await using NpgsqlCommand command = _dataSource.CreateCommand(InsertSql);
        command.Parameters.AddWithValue(article.Id);
        command.Parameters.AddWithValue(article.CanonicalUrl.AbsoluteUri);
        command.Parameters.AddWithValue(article.Title);
        command.Parameters.AddWithValue(article.Source);
        NpgsqlParameter externalId = new()
        {
            NpgsqlDbType = NpgsqlDbType.Text,
            Value = (object?)article.ExternalId ?? DBNull.Value
        };
        command.Parameters.Add(externalId);
        command.Parameters.AddWithValue(article.PublishedAt);
        command.Parameters.AddWithValue(article.CollectedAt);
        command.Parameters.AddWithValue((int)article.Assessment.Topics);
        command.Parameters.AddWithValue((short)article.Assessment.PrimaryTopic);
        command.Parameters.AddWithValue((short)article.Assessment.SourceTrust);
        command.Parameters.AddWithValue((short)article.Assessment.TopicInterest);
        command.Parameters.AddWithValue((short)article.Assessment.PracticalImpact);
        command.Parameters.AddWithValue((short)article.Assessment.Freshness);
        command.Parameters.AddWithValue(article.Assessment.BaseScore);
        command.Parameters.AddWithValue(article.Assessment.ProfileVersion);

        int affectedRows = await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);

        return affectedRows == 1;
    }
}
