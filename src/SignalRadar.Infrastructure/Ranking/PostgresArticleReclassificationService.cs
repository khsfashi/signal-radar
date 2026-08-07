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

            List<AssessmentUpdate> changes = new(batch.Count);

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

                if (AssessmentChanged(stored, assessment))
                {
                    changes.Add(new AssessmentUpdate(stored.Id, assessment));
                }
            }

            if (changes.Count > 0)
            {
                updated += await UpdateAssessmentsAsync(
                    changes,
                    cancellationToken).ConfigureAwait(false);
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

    private async ValueTask<int> UpdateAssessmentsAsync(
        IReadOnlyList<AssessmentUpdate> changes,
        CancellationToken cancellationToken)
    {
        Guid[] ids = new Guid[changes.Count];
        int[] topics = new int[changes.Count];
        short[] primaryTopics = new short[changes.Count];
        short[] sourceTrust = new short[changes.Count];
        short[] topicInterest = new short[changes.Count];
        short[] practicalImpact = new short[changes.Count];
        short[] freshness = new short[changes.Count];
        decimal[] baseScores = new decimal[changes.Count];
        string[] profileVersions = new string[changes.Count];

        for (int index = 0; index < changes.Count; index++)
        {
            AssessmentUpdate change = changes[index];
            ids[index] = change.Id;
            topics[index] = (int)change.Assessment.Topics;
            primaryTopics[index] = (short)change.Assessment.PrimaryTopic;
            sourceTrust[index] = (short)change.Assessment.SourceTrust;
            topicInterest[index] = (short)change.Assessment.TopicInterest;
            practicalImpact[index] = (short)change.Assessment.PracticalImpact;
            freshness[index] = (short)change.Assessment.Freshness;
            baseScores[index] = change.Assessment.BaseScore;
            profileVersions[index] = change.Assessment.ProfileVersion;
        }

        const string sql = """
            UPDATE articles AS article
            SET topics = changed.topics,
                primary_topic = changed.primary_topic,
                source_trust = changed.source_trust,
                topic_interest = changed.topic_interest,
                practical_impact = changed.practical_impact,
                freshness = changed.freshness,
                base_score = changed.base_score,
                ranking_profile_version = changed.ranking_profile_version
            FROM unnest(
                $1::uuid[],
                $2::integer[],
                $3::smallint[],
                $4::smallint[],
                $5::smallint[],
                $6::smallint[],
                $7::smallint[],
                $8::numeric[],
                $9::text[])
                AS changed(
                    id,
                    topics,
                    primary_topic,
                    source_trust,
                    topic_interest,
                    practical_impact,
                    freshness,
                    base_score,
                    ranking_profile_version)
            WHERE article.id = changed.id;
            """;
        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(ids);
        command.Parameters.AddWithValue(topics);
        command.Parameters.AddWithValue(primaryTopics);
        command.Parameters.AddWithValue(sourceTrust);
        command.Parameters.AddWithValue(topicInterest);
        command.Parameters.AddWithValue(practicalImpact);
        command.Parameters.AddWithValue(freshness);
        command.Parameters.AddWithValue(baseScores);
        command.Parameters.AddWithValue(profileVersions);
        int affected = await command
            .ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (affected != changes.Count)
        {
            throw new InvalidOperationException(
                $"Expected to update {changes.Count} article assessments but updated {affected}.");
        }

        return affected;
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

    private sealed record AssessmentUpdate(
        Guid Id,
        ArticleAssessment Assessment);

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
