using Npgsql;
using SignalRadar.Application.Summaries;
using SignalRadar.Infrastructure.Database;
using SignalRadar.Infrastructure.Summaries;
using Xunit;

namespace SignalRadar.Infrastructure.Tests.Summaries;

public sealed class PostgresArticleSummaryCacheTests
{
    [Fact]
    public async Task CachePersistsStructuredSummaryAndKeepsFirstWriter()
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
            "TRUNCATE TABLE article_summary_cache;"))
        {
            await reset.ExecuteNonQueryAsync(CancellationToken.None);
        }

        DateTimeOffset generatedAt = DateTimeOffset.UtcNow;
        string inputHash = new('A', 64);
        ArticleSummaryCacheEntry first = new(
            inputHash,
            "provider",
            "model",
            "prompt-v1",
            "ko",
            2,
            new ArticleSummaryContent(
                "First summary",
                "Overview",
                ["Point"],
                "Impact",
                ["Watch"],
                ["Caveat"]),
            generatedAt,
            "response-1");
        ArticleSummaryCacheEntry second = first with
        {
            Content = first.Content with { Title = "Second summary" },
            ProviderResponseId = "response-2"
        };
        PostgresArticleSummaryCache cache = new(dataSource);

        await cache.StoreAsync(first, CancellationToken.None);
        await cache.StoreAsync(second, CancellationToken.None);
        ArticleSummaryCacheEntry? loaded = await cache.TryGetAsync(
            inputHash,
            CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("First summary", loaded.Content.Title);
        Assert.Equal("response-1", loaded.ProviderResponseId);
        Assert.Equal(first.ArticleCount, loaded.ArticleCount);
        Assert.InRange(
            (loaded.GeneratedAt - generatedAt).Duration(),
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(1));
    }
}
