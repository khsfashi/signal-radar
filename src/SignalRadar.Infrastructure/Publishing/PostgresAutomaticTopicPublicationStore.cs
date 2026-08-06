using Npgsql;
using SignalRadar.Application.Publishing;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Infrastructure.Publishing;

public sealed class PostgresAutomaticTopicPublicationStore
    : IAutomaticTopicPublicationStore
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresAutomaticTopicPublicationStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async ValueTask<DateTimeOffset> GetOrCreateActivationTimeAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO automatic_topic_publishing_state (singleton, activated_at)
            VALUES (TRUE, $1)
            ON CONFLICT (singleton) DO UPDATE
            SET activated_at = automatic_topic_publishing_state.activated_at
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
                "Automatic topic publishing activation time was not returned.")
        };
    }

    public async ValueTask<IReadOnlyList<AutomaticTopicPublicationCandidate>> GetCandidatesAsync(
        AutomaticTopicPublicationQuery query,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateQuery(query);

        const string sql = """
            WITH feedback AS (
                SELECT article_id,
                    COALESCE(SUM(weight), 0)::integer AS total_weight
                FROM article_feedback
                GROUP BY article_id
            ),
            scored AS (
                SELECT article.id,
                    article.title,
                    article.canonical_url,
                    article.source,
                    article.published_at,
                    article.collected_at,
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
            )
            SELECT scored.id,
                scored.title,
                scored.canonical_url,
                scored.source,
                scored.published_at,
                scored.collected_at,
                scored.topics,
                scored.primary_topic,
                scored.base_score,
                scored.feedback_weight,
                scored.effective_score
            FROM scored
            LEFT JOIN automatic_topic_publication_receipts AS receipt
                ON receipt.article_id = scored.id
                AND receipt.channel_id = $4
                AND receipt.publication_kind = $5
            WHERE scored.collected_at >= $1
                AND scored.primary_topic = $2
                AND scored.effective_score >= $3
                AND (
                    receipt.article_id IS NULL
                    OR (
                        receipt.completed_at IS NULL
                        AND receipt.lease_expires_at <= $7))
            ORDER BY scored.collected_at,
                scored.published_at,
                scored.id
            LIMIT $6;
            """;

        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(query.CollectedAfter.ToUniversalTime());
        command.Parameters.AddWithValue((short)query.Topic);
        command.Parameters.AddWithValue(query.MinimumScore);
        command.Parameters.AddWithValue(ToNumeric(query.ChannelId));
        command.Parameters.AddWithValue(query.PublicationKind);
        command.Parameters.AddWithValue(query.Limit);
        command.Parameters.AddWithValue(now.ToUniversalTime());
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        List<AutomaticTopicPublicationCandidate> candidates = new(query.Limit);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string canonicalUrl = reader.GetString(2);

            if (!Uri.TryCreate(canonicalUrl, UriKind.Absolute, out Uri? canonicalUri))
            {
                throw new InvalidDataException(
                    $"Stored canonical URL '{canonicalUrl}' is invalid.");
            }

            candidates.Add(new AutomaticTopicPublicationCandidate(
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

        return candidates;
    }

    public async ValueTask<AutomaticTopicPublicationLease?> TryBeginAsync(
        Guid articleId,
        ulong channelId,
        string publicationKind,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(articleId, channelId, publicationKind);

        if (leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        Guid token = Guid.NewGuid();
        string normalizedKind = publicationKind.Trim();
        const string sql = """
            INSERT INTO automatic_topic_publication_receipts (
                article_id,
                channel_id,
                publication_kind,
                lease_token,
                lease_expires_at,
                attempt_count)
            VALUES ($1, $2, $3, $4, $5 + $6, 1)
            ON CONFLICT (article_id, channel_id, publication_kind) DO UPDATE
            SET lease_token = EXCLUDED.lease_token,
                lease_expires_at = EXCLUDED.lease_expires_at,
                attempt_count = automatic_topic_publication_receipts.attempt_count + 1,
                last_error = NULL
            WHERE automatic_topic_publication_receipts.completed_at IS NULL
                AND automatic_topic_publication_receipts.lease_expires_at <= $5
            RETURNING lease_token;
            """;

        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(articleId);
        command.Parameters.AddWithValue(ToNumeric(channelId));
        command.Parameters.AddWithValue(normalizedKind);
        command.Parameters.AddWithValue(token);
        command.Parameters.AddWithValue(now.ToUniversalTime());
        command.Parameters.AddWithValue(leaseDuration);
        object? result = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return result is Guid returnedToken
            ? new AutomaticTopicPublicationLease(
                articleId,
                channelId,
                normalizedKind,
                returnedToken)
            : null;
    }

    public async ValueTask CompleteAsync(
        AutomaticTopicPublicationLease lease,
        DateTimeOffset deliveredAt,
        ulong discordResourceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);

        if (discordResourceId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(discordResourceId));
        }

        const string sql = """
            UPDATE automatic_topic_publication_receipts
            SET completed_at = $5,
                discord_resource_id = $6,
                lease_expires_at = $5,
                last_error = NULL
            WHERE article_id = $1
                AND channel_id = $2
                AND publication_kind = $3
                AND lease_token = $4
                AND completed_at IS NULL;
            """;

        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        AddLeaseParameters(command, lease);
        command.Parameters.AddWithValue(deliveredAt.ToUniversalTime());
        command.Parameters.AddWithValue(ToNumeric(discordResourceId));
        int affected = await command.ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);

        if (affected != 1)
        {
            throw new InvalidOperationException(
                "The automatic topic publication lease was lost before completion.");
        }
    }

    public async ValueTask FailAsync(
        AutomaticTopicPublicationLease lease,
        DateTimeOffset failedAt,
        TimeSpan retryDelay,
        string error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        if (retryDelay < TimeSpan.Zero || retryDelay > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelay));
        }

        string normalizedError = error.Trim();

        if (normalizedError.Length > 1000)
        {
            normalizedError = normalizedError[..1000];
        }

        const string sql = """
            UPDATE automatic_topic_publication_receipts
            SET last_failed_at = $5,
                last_error = $6,
                lease_expires_at = $5 + $7
            WHERE article_id = $1
                AND channel_id = $2
                AND publication_kind = $3
                AND lease_token = $4
                AND completed_at IS NULL;
            """;

        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        AddLeaseParameters(command, lease);
        command.Parameters.AddWithValue(failedAt.ToUniversalTime());
        command.Parameters.AddWithValue(normalizedError);
        command.Parameters.AddWithValue(retryDelay);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddLeaseParameters(
        NpgsqlCommand command,
        AutomaticTopicPublicationLease lease)
    {
        command.Parameters.AddWithValue(lease.ArticleId);
        command.Parameters.AddWithValue(ToNumeric(lease.ChannelId));
        command.Parameters.AddWithValue(lease.PublicationKind);
        command.Parameters.AddWithValue(lease.LeaseToken);
    }

    private static decimal ToNumeric(ulong value)
    {
        return value;
    }

    private static void ValidateQuery(AutomaticTopicPublicationQuery query)
    {
        ValidateChannelAndKind(query.ChannelId, query.PublicationKind);
        int topicValue = (int)query.Topic;

        if (topicValue <= 0 || (topicValue & (topicValue - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query.Topic));
        }

        if (query.MinimumScore is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(query.MinimumScore));
        }

        if (query.Limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(query.Limit));
        }
    }

    private static void ValidateIdentity(
        Guid articleId,
        ulong channelId,
        string publicationKind)
    {
        if (articleId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(articleId));
        }

        ValidateChannelAndKind(channelId, publicationKind);
    }

    private static void ValidateChannelAndKind(
        ulong channelId,
        string publicationKind)
    {
        if (channelId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channelId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(publicationKind);

        if (publicationKind.Trim().Length > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(publicationKind));
        }
    }
}
