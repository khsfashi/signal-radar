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
            collected_at)
        VALUES ($1, $2, $3, $4, $5, $6, $7)
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
        NpgsqlParameter externalId = command.Parameters.Add(NpgsqlDbType.Text);
        externalId.Value = (object?)article.ExternalId ?? DBNull.Value;
        command.Parameters.AddWithValue(article.PublishedAt);
        command.Parameters.AddWithValue(article.CollectedAt);

        int affectedRows = await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);

        return affectedRows == 1;
    }
}
