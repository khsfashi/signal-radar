using Npgsql;
using SignalRadar.Application.Articles;
using SignalRadar.Application.Discord;
using SignalRadar.Domain.Articles;
using SignalRadar.Infrastructure.Articles;
using SignalRadar.Infrastructure.Database;
using SignalRadar.Infrastructure.Discord;
using SignalRadar.Infrastructure.Ranking;
using Xunit;

namespace SignalRadar.Infrastructure.Tests.Database;

public sealed class PostgresPersistenceTests
{
    [Fact]
    public async Task StoresPersistDuplicateGuardsAcrossInstances()
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

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Article article = Article.Create(
            "PostgreSQL persistence",
            new Uri("https://example.com/postgres"),
            "integration-test",
            now,
            now,
            "external-1");
        Article sameExternalItemAtDifferentUrl = Article.Create(
            "PostgreSQL persistence mirror",
            new Uri("https://mirror.example.com/postgres"),
            "integration-test",
            now,
            now,
            "external-1");

        PostgresArticleInbox firstArticleStore = new(dataSource);
        PostgresArticleInbox secondArticleStore = new(dataSource);

        Assert.True(await firstArticleStore.TryAddAsync(
            article,
            CancellationToken.None));
        Assert.False(await secondArticleStore.TryAddAsync(
            article,
            CancellationToken.None));
        Assert.False(await secondArticleStore.TryAddAsync(
            sameExternalItemAtDifferentUrl,
            CancellationToken.None));

        PostgresArticleSaveStore firstSaveStore = new(dataSource);
        PostgresArticleSaveStore secondSaveStore = new(dataSource);
        Assert.True(await firstSaveStore.TryAddAsync(
            article.Id,
            "discord:123",
            now,
            CancellationToken.None));
        Assert.False(await secondSaveStore.TryAddAsync(
            article.Id,
            "discord:123",
            now.AddMinutes(1),
            CancellationToken.None));

        IReadOnlyList<SavedArticle> saved = await secondSaveStore.GetSavedAsync(
            new SavedArticleQuery(
                now.AddDays(-1),
                ArticleTopic.None,
                10,
                "discord:123"),
            CancellationToken.None);
        Assert.Single(saved);
        Assert.Equal(article.Id, saved[0].ArticleId);
        Assert.InRange(
            saved[0].SavedAt,
            now.AddMilliseconds(-1),
            now.AddMilliseconds(1));
        Assert.True(await secondSaveStore.RemoveAsync(
            article.Id,
            "discord:123",
            CancellationToken.None));

        PostgresDiscordMessageReceiptStore firstReceiptStore = new(
            dataSource,
            TimeSpan.FromMinutes(1));
        PostgresDiscordMessageReceiptStore secondReceiptStore = new(
            dataSource,
            TimeSpan.FromMinutes(1));

        DiscordMessageReceiptLease? lease = await firstReceiptStore.TryBeginAsync(
            ulong.MaxValue,
            CancellationToken.None);

        Assert.NotNull(lease);
        await firstReceiptStore.CompleteAsync(
            lease,
            CancellationToken.None);
        Assert.Null(await secondReceiptStore.TryBeginAsync(
            ulong.MaxValue,
            CancellationToken.None));
    }

    private static async Task ResetTablesAsync(NpgsqlDataSource dataSource)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "TRUNCATE TABLE automatic_topic_publication_receipts, article_content_cache, article_saves, article_feedback, discord_message_receipts, articles;");
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
