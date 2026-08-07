using Npgsql;
using NpgsqlTypes;
using SignalRadar.Application.Summaries;

namespace SignalRadar.Infrastructure.Summaries;

public sealed class PostgresArticleContentCache : IArticleContentCache
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresArticleContentCache(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async ValueTask<ArticleContentSnapshot?> TryGetFreshAsync(
        Guid articleId,
        Uri canonicalUrl,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ValidateArticleId(articleId);
        ValidateCanonicalUrl(canonicalUrl);
        const string sql = """
            SELECT status,
                   content_text,
                   content_hash,
                   fetched_at,
                   refresh_after,
                   http_status,
                   content_type,
                   detail
            FROM article_content_cache
            WHERE article_id = $1
              AND canonical_url = $2
              AND refresh_after > $3;
            """;

        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(articleId);
        command.Parameters.AddWithValue(canonicalUrl.AbsoluteUri);
        command.Parameters.AddWithValue(now.ToUniversalTime());
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        ArticleContentStatus status = checked((ArticleContentStatus)reader.GetInt16(0));
        return new ArticleContentSnapshot(
            articleId,
            canonicalUrl,
            status,
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.IsDBNull(5) ? null : reader.GetInt16(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7));
    }

    public async ValueTask StoreAsync(
        ArticleContentSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        ValidateSnapshot(snapshot);
        const string sql = """
            INSERT INTO article_content_cache (
                article_id,
                canonical_url,
                status,
                content_text,
                content_hash,
                fetched_at,
                refresh_after,
                http_status,
                content_type,
                detail)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
            ON CONFLICT (article_id) DO UPDATE SET
                canonical_url = EXCLUDED.canonical_url,
                status = EXCLUDED.status,
                content_text = EXCLUDED.content_text,
                content_hash = EXCLUDED.content_hash,
                fetched_at = EXCLUDED.fetched_at,
                refresh_after = EXCLUDED.refresh_after,
                http_status = EXCLUDED.http_status,
                content_type = EXCLUDED.content_type,
                detail = EXCLUDED.detail;
            """;

        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(snapshot.ArticleId);
        command.Parameters.AddWithValue(snapshot.CanonicalUrl.AbsoluteUri);
        command.Parameters.AddWithValue((short)snapshot.Status);
        command.Parameters.Add(CreateNullableTextParameter(snapshot.Text));
        command.Parameters.Add(CreateNullableTextParameter(snapshot.ContentHash));
        command.Parameters.AddWithValue(snapshot.FetchedAt.ToUniversalTime());
        command.Parameters.AddWithValue(snapshot.RefreshAfter.ToUniversalTime());
        command.Parameters.Add(CreateNullableSmallintParameter(snapshot.HttpStatusCode));
        command.Parameters.Add(CreateNullableTextParameter(snapshot.ContentType));
        command.Parameters.Add(CreateNullableTextParameter(snapshot.Detail));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static NpgsqlParameter CreateNullableTextParameter(string? value)
    {
        return new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Text,
            Value = value is null ? DBNull.Value : value
        };
    }

    private static NpgsqlParameter CreateNullableSmallintParameter(int? value)
    {
        return new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Smallint,
            Value = value.HasValue ? checked((short)value.Value) : DBNull.Value
        };
    }

    private static void ValidateSnapshot(ArticleContentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateArticleId(snapshot.ArticleId);
        ValidateCanonicalUrl(snapshot.CanonicalUrl);

        if (!Enum.IsDefined(snapshot.Status))
        {
            throw new ArgumentOutOfRangeException(nameof(snapshot.Status));
        }

        if (snapshot.RefreshAfter < snapshot.FetchedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(snapshot.RefreshAfter),
                "The refresh time cannot precede the fetch time.");
        }

        if (snapshot.HasExtractedContent)
        {
            ValidateHash(snapshot.ContentHash);
        }
        else if (snapshot.Text is not null || snapshot.ContentHash is not null)
        {
            throw new ArgumentException(
                "Only successfully extracted content can contain text and a content hash.",
                nameof(snapshot));
        }

        if (snapshot.HttpStatusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(snapshot.HttpStatusCode));
        }

        ValidateOptionalLength(snapshot.ContentType, 200, nameof(snapshot.ContentType));
        ValidateOptionalLength(snapshot.Detail, 500, nameof(snapshot.Detail));
    }

    private static void ValidateHash(string? value)
    {
        if (value is null || value.Length != 64)
        {
            throw new ArgumentException(
                "The article content hash must contain 64 uppercase hexadecimal characters.",
                nameof(value));
        }

        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];

            if (!char.IsAsciiHexDigit(character) || char.IsLower(character))
            {
                throw new ArgumentException(
                    "The article content hash must contain uppercase hexadecimal characters.",
                    nameof(value));
            }
        }
    }

    private static void ValidateArticleId(Guid articleId)
    {
        if (articleId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(articleId));
        }
    }

    private static void ValidateCanonicalUrl(Uri canonicalUrl)
    {
        ArgumentNullException.ThrowIfNull(canonicalUrl);

        if (!canonicalUrl.IsAbsoluteUri
            || canonicalUrl.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "Article content URLs must be absolute HTTP or HTTPS URLs.",
                nameof(canonicalUrl));
        }
    }

    private static void ValidateOptionalLength(
        string? value,
        int maximumLength,
        string parameterName)
    {
        if (value is not null && value.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
