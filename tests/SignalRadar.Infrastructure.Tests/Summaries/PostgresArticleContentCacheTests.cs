using System.Security.Cryptography;
using System.Text;
using Npgsql;
using SignalRadar.Application.Summaries;
using SignalRadar.Domain.Articles;
using SignalRadar.Infrastructure.Articles;
using SignalRadar.Infrastructure.Database;
using SignalRadar.Infrastructure.Summaries;
using Xunit;

namespace SignalRadar.Infrastructure.Tests.Summaries;

public sealed class PostgresArticleContentCacheTests
{
    [Fact]
    public async Task CachePersistsContentAndHonorsRefreshTime()
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
        await using (NpgsqlCommand reset = dataSource.CreateCommand(
            "TRUNCATE TABLE article_content_cache, article_feedback, article_saves, discord_message_receipts, articles;"))
        {
            await reset.ExecuteNonQueryAsync(CancellationToken.None);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Article article = Article.Create(
            "Cached article body",
            new Uri($"https://example.com/content/{Guid.NewGuid():N}"),
            "integration-content",
            now,
            now,
            Guid.NewGuid().ToString("N"));
        PostgresArticleInbox inbox = new(dataSource);
        Assert.True(await inbox.TryAddAsync(article, CancellationToken.None));

        string text = "A deterministic article body stored for summary reuse.";
        string hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        ArticleContentSnapshot snapshot = new(
            article.Id,
            article.CanonicalUrl,
            ArticleContentStatus.Extracted,
            text,
            hash,
            now,
            now.AddHours(2),
            200,
            "text/html",
            "integration test");
        PostgresArticleContentCache cache = new(dataSource);

        await cache.StoreAsync(snapshot, CancellationToken.None);
        ArticleContentSnapshot? fresh = await cache.TryGetFreshAsync(
            article.Id,
            article.CanonicalUrl,
            now.AddHours(1),
            CancellationToken.None);
        ArticleContentSnapshot? expired = await cache.TryGetFreshAsync(
            article.Id,
            article.CanonicalUrl,
            now.AddHours(3),
            CancellationToken.None);

        Assert.NotNull(fresh);
        Assert.Equal(text, fresh.Text);
        Assert.Equal(hash, fresh.ContentHash);
        Assert.Null(expired);
    }
}
