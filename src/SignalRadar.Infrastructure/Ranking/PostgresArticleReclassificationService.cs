using Npgsql;
using SignalRadar.Application.Articles;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Infrastructure.Ranking;

public sealed class PostgresArticleReclassificationService
    : IArticleReclassificationService
{
    private const int BatchSize = 500;
    private readonly NpgsqlDataSource _dataSource;
    private readonly IArticleAssessmentPolicy _assessmentPolicy;
    private readonly TimeProvider _timeProvider;

    public PostgresArticleReclassificationService(
        NpgsqlDataSource dataSource,
        IArticleAssessmentPolicy assessmentPolicy,
        TimeProvider timeProvider)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _assessmentPolicy = assessmentPolicy
            ?? throw new ArgumentNullException(nameof(assessmentPolicy));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async ValueTask<ArticleReclassificationResult> ReclassifyAsync(
        TimeSpan age,
        CancellationToken cancellationToken)
    {
        if (age <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(age));
        }

        DateTimeOffset cutoff = _timeProvider.GetUtcNow().Subtract(age);
        DateTimeOffset? lastCollectedAt = null;
        Guid? lastId = null;
        int scanned = 0;
        int updated = 0;

        while (true)
        {
            IReadOnlyList<StoredArticle> batch = await ReadBatchAsync(
                cutoff,
                lastCollectedAt,
                lastId,
                cancellationToken).ConfigureAwait(false);

            if (batch.Count == 0)
            {
                break;
            }

            for (int index = 0; index < batch.Count; index++)
            {
                StoredArticle stored = batch[index];
                CollectedArticleCandidate candidate = new(
                    stored.Title,
                    stored.CanonicalUrl.AbsoluteUri,
                    stored.Source,
                    stored.PublishedAt,
                    stored.ExternalId);
                ArticleAssessment assessment = _assessmentPolicy.Assess(
                    candidate,
                    stored.CanonicalUrl,
                    stored.CollectedAt);
                scanned++;

                if (!AssessmentChanged(stored, assessment))
                {
                    continue;
                }

                await UpdateAssessmentAsync(
                    stored.Id,
                    assessment,
                    cancellationToken).ConfigureAwait(false);
                updated++;
            }

            StoredArticle last = batch[^1];
            lastCollectedAt = last.CollectedAt;
            lastId = last.Id;

            if (batch.Count < BatchSize)
            {
                break;
            }
        }

        return new ArticleReclassificationResult(scanned, updated);
    }

    private async ValueTask<IReadOnlyList<StoredArticle>> ReadBatchAsync(
        DateTimeOffset cutoff,
        DateTimeOffset? lastCollectedAt,
        Guid? lastId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id,
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
                ranking_profile_version
            FROM articles
            WHERE collected_at >= $1
              AND (
                    $2::timestamptz IS NULL
                    OR (collected_at, id) > ($2, $3::uuid)
                  )
            ORDER BY collected_at, id
            LIMIT $4;
            """;
        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(cutoff);
        command.Parameters.AddWithValue((object?)lastCollectedAt ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)lastId ?? DBNull.Value);
        command.Parameters.AddWithValue(BatchSize);
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<StoredArticle> articles = new(BatchSize);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string canonicalUrlValue = reader.GetString(1);

            if (!Uri.TryCreate(
                    canonicalUrlValue,
                    UriKind.Absolute,
                    out Uri? canonicalUrl)
                || canonicalUrl.Scheme is not ("http" or "https"))
            {
                throw new InvalidDataException(
                    $"Stored article URL '{canonicalUrlValue}' is invalid.");
            }

            articles.Add(new StoredArticle(
                reader.GetGuid(0),
                canonicalUrl,
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                (ArticleTopic)reader.GetInt32(7),
                (ArticleTopic)reader.GetInt16(8),
                reader.GetInt16(9),
                reader.GetInt16(10),
                reader.GetInt16(11),
                reader.GetInt16(12),
                reader.GetDecimal(13),
                reader.GetString(14)));
        }

        return articles;
    }

    private async ValueTask UpdateAssessmentAsync(
        Guid articleId,
        ArticleAssessment assessment,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE articles
            SET topics = $2,
                primary_topic = $3,
                source_trust = $4,
                topic_interest = $5,
                practical_impact = $6,
                freshness = $7,
                base_score = $8,
                ranking_profile_version = $9
            WHERE id = $1;
            """;
        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(articleId);
        command.Parameters.AddWithValue((int)assessment.Topics);
        command.Parameters.AddWithValue((short)assessment.PrimaryTopic);
        command.Parameters.AddWithValue((short)assessment.SourceTrust);
        command.Parameters.AddWithValue((short)assessment.TopicInterest);
        command.Parameters.AddWithValue((short)assessment.PracticalImpact);
        command.Parameters.AddWithValue((short)assessment.Freshness);
        command.Parameters.AddWithValue(assessment.BaseScore);
        command.Parameters.AddWithValue(assessment.ProfileVersion);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool AssessmentChanged(
        StoredArticle stored,
        ArticleAssessment assessment)
    {
        return stored.Topics != assessment.Topics
            || stored.PrimaryTopic != assessment.PrimaryTopic
            || stored.SourceTrust != assessment.SourceTrust
            || stored.TopicInterest != assessment.TopicInterest
            || stored.PracticalImpact != assessment.PracticalImpact
            || stored.Freshness != assessment.Freshness
            || stored.BaseScore != assessment.BaseScore
            || !string.Equals(
                stored.ProfileVersion,
                assessment.ProfileVersion,
                StringComparison.Ordinal);
    }

    private sealed record StoredArticle(
        Guid Id,
        Uri CanonicalUrl,
        string Title,
        string Source,
        string? ExternalId,
        DateTimeOffset PublishedAt,
        DateTimeOffset CollectedAt,
        ArticleTopic Topics,
        ArticleTopic PrimaryTopic,
        int SourceTrust,
        int TopicInterest,
        int PracticalImpact,
        int Freshness,
        decimal BaseScore,
        string ProfileVersion);
}
