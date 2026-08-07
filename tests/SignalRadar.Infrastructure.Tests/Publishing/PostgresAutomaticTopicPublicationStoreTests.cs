using Npgsql;
using SignalRadar.Application.Publishing;
using SignalRadar.Domain.Articles;
using SignalRadar.Infrastructure.Database;
using SignalRadar.Infrastructure.Publishing;
using Xunit;

namespace SignalRadar.Infrastructure.Tests.Publishing;

public sealed class PostgresAutomaticTopicPublicationStoreTests
{
    [Fact]
    public async Task CandidateLeaseRetriesAndCompletedPublicationIsNotReturned()
    {
        string? connectionString = Environment.GetEnvironmentVariable(
            "SIGNAL_RADAR_TEST_DATABASE_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(
            connectionString);
        PostgresDatabaseMigrator migrator = new(dataSource);
        await migrator.MigrateAsync(CancellationToken.None);
        PostgresAutomaticTopicPublicationStore store = new(dataSource);
        DateTimeOffset activation = await store.GetOrCreateActivationTimeAsync(
            DateTimeOffset.UtcNow,
            CancellationToken.None);
        DateTimeOffset collectedAt = activation.AddMinutes(1);
        Guid articleId = Guid.NewGuid();
        string canonicalUrl = $"https://example.com/topic-publishing/{articleId:N}";

        await InsertArticleAsync(
            dataSource,
            articleId,
            canonicalUrl,
            collectedAt);

        try
        {
            const ulong channelId = ulong.MaxValue;
            AutomaticTopicPublicationQuery query = new(
                ArticleTopic.ArtificialIntelligence,
                channelId,
                activation,
                60m,
                10,
                DiscordAutomaticTopicPublishingKind);
            IReadOnlyList<AutomaticTopicPublicationCandidate> firstCandidates =
                await store.GetCandidatesAsync(
                    query,
                    collectedAt.AddMinutes(1),
                    CancellationToken.None);
            AutomaticTopicPublicationCandidate candidate = Assert.Single(firstCandidates);
            Assert.Equal(articleId, candidate.ArticleId);

            AutomaticTopicPublicationLease? firstLease = await store.TryBeginAsync(
                articleId,
                channelId,
                DiscordAutomaticTopicPublishingKind,
                collectedAt.AddMinutes(1),
                TimeSpan.FromMinutes(5),
                CancellationToken.None);
            Assert.NotNull(firstLease);
            Assert.Null(await store.TryBeginAsync(
                articleId,
                channelId,
                DiscordAutomaticTopicPublishingKind,
                collectedAt.AddMinutes(2),
                TimeSpan.FromMinutes(5),
                CancellationToken.None));

            await store.FailAsync(
                firstLease,
                collectedAt.AddMinutes(2),
                TimeSpan.FromMinutes(1),
                "temporary Discord failure",
                CancellationToken.None);
            Assert.Empty(await store.GetCandidatesAsync(
                query,
                collectedAt.AddMinutes(2).AddSeconds(30),
                CancellationToken.None));

            IReadOnlyList<AutomaticTopicPublicationCandidate> retryCandidates =
                await store.GetCandidatesAsync(
                    query,
                    collectedAt.AddMinutes(3),
                    CancellationToken.None);
            Assert.Single(retryCandidates);
            AutomaticTopicPublicationLease? retryLease = await store.TryBeginAsync(
                articleId,
                channelId,
                DiscordAutomaticTopicPublishingKind,
                collectedAt.AddMinutes(3),
                TimeSpan.FromMinutes(5),
                CancellationToken.None);
            Assert.NotNull(retryLease);
            await store.CompleteAsync(
                retryLease,
                collectedAt.AddMinutes(4),
                ulong.MaxValue,
                CancellationToken.None);

            Assert.Empty(await store.GetCandidatesAsync(
                query,
                collectedAt.AddDays(1),
                CancellationToken.None));
        }
        finally
        {
            await using NpgsqlCommand cleanup = dataSource.CreateCommand(
                "DELETE FROM articles WHERE id = $1;");
            cleanup.Parameters.AddWithValue(articleId);
            await cleanup.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private const string DiscordAutomaticTopicPublishingKind =
        "discord-topic-article-v1";

    private static async Task InsertArticleAsync(
        NpgsqlDataSource dataSource,
        Guid articleId,
        string canonicalUrl,
        DateTimeOffset collectedAt)
    {
        const string sql = """
            INSERT INTO articles (
                id,
                canonical_url,
                title,
                source,
                published_at,
                collected_at,
                topics,
                primary_topic,
                base_score,
                ranking_profile_version)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10);
            """;
        await using NpgsqlCommand command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(articleId);
        command.Parameters.AddWithValue(canonicalUrl);
        command.Parameters.AddWithValue("Automatic topic publishing test article");
        command.Parameters.AddWithValue("test-source");
        command.Parameters.AddWithValue(collectedAt);
        command.Parameters.AddWithValue(collectedAt);
        command.Parameters.AddWithValue((int)ArticleTopic.ArtificialIntelligence);
        command.Parameters.AddWithValue((short)ArticleTopic.ArtificialIntelligence);
        command.Parameters.AddWithValue(75m);
        command.Parameters.AddWithValue("test-profile");
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
