using Npgsql;
using SignalRadar.Domain.Articles;
using SignalRadar.Infrastructure.Articles;
using SignalRadar.Infrastructure.Database;
using SignalRadar.Infrastructure.Ranking;
using Xunit;

namespace SignalRadar.Infrastructure.Tests.Ranking;

public sealed class PostgresArticleReclassificationServiceTests
{
    [Fact]
    public async Task ReclassifyAsync_UsesCurrentPolicyAndOriginalCollectedTime()
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
        await ResetTablesAsync(dataSource);

        DateTimeOffset collectedAt = new(
            2026,
            8,
            7,
            6,
            0,
            0,
            TimeSpan.Zero);
        Article article = Article.Create(
            "코스닥, 엿새 만에 하락 전환 800선 아래로",
            new Uri("https://v.daum.net/v/20260807150000000"),
            "korea-markets",
            collectedAt.AddHours(-1),
            collectedAt);
        PostgresArticleInbox inbox = new(dataSource);
        Assert.True(await inbox.TryAddAsync(article, CancellationToken.None));

        RuleBasedArticleAssessmentPolicy policy = new(
            ArticleRankingProfile.CreateDefault());
        PostgresArticleReclassificationService service = new(
            dataSource,
            policy,
            new FixedTimeProvider(collectedAt.AddDays(10)));

        var first = await service.ReclassifyAsync(
            TimeSpan.FromDays(30),
            CancellationToken.None);

        Assert.Equal(1, first.ScannedCount);
        Assert.Equal(1, first.UpdatedCount);

        await using NpgsqlCommand read = dataSource.CreateCommand(
            "SELECT topics, primary_topic, freshness, ranking_profile_version FROM articles WHERE id = $1;");
        read.Parameters.AddWithValue(article.Id);
        await using NpgsqlDataReader reader = await read.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        ArticleTopic topics = (ArticleTopic)reader.GetInt32(0);
        Assert.True(topics.HasFlag(ArticleTopic.Markets));
        Assert.False(topics.HasFlag(ArticleTopic.DeveloperTools));
        Assert.Equal(ArticleTopic.Markets, (ArticleTopic)reader.GetInt16(1));
        Assert.Equal(100, reader.GetInt16(2));
        Assert.Equal("default-v1", reader.GetString(3));
        await reader.DisposeAsync();

        var second = await service.ReclassifyAsync(
            TimeSpan.FromDays(30),
            CancellationToken.None);
        Assert.Equal(1, second.ScannedCount);
        Assert.Equal(0, second.UpdatedCount);
    }

    private static async Task ResetTablesAsync(NpgsqlDataSource dataSource)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "TRUNCATE TABLE automatic_topic_publication_receipts, article_content_cache, article_saves, article_feedback, discord_message_receipts, articles;");
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
