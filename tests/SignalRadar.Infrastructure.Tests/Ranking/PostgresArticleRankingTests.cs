using Npgsql;
using SignalRadar.Application.Articles;
using SignalRadar.Domain.Articles;
using SignalRadar.Infrastructure.Articles;
using SignalRadar.Infrastructure.Database;
using SignalRadar.Infrastructure.Ranking;
using Xunit;

namespace SignalRadar.Infrastructure.Tests.Ranking;

public sealed class PostgresArticleRankingTests
{
    [Fact]
    public async Task FeedbackAdjustsEffectiveArticleScore()
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

        DateTimeOffset now = DateTimeOffset.UtcNow;
        ArticleAssessment assessment = new(
            ArticleTopic.ArtificialIntelligence,
            ArticleTopic.ArtificialIntelligence,
            80,
            100,
            60,
            100,
            84m,
            "integration-v1");
        Article article = Article.Create(
            "Unique AI ranking article",
            new Uri($"https://example.com/ranking/{Guid.NewGuid():N}"),
            "integration-ranking",
            now,
            now,
            externalId: Guid.NewGuid().ToString("N"),
            assessment);
        PostgresArticleInbox inbox = new(dataSource);
        Assert.True(await inbox.TryAddAsync(article, CancellationToken.None));

        PostgresArticleFeedbackStore feedbackStore = new(dataSource);
        await feedbackStore.SetAsync(
            article.Id,
            "actor-positive",
            ArticleFeedbackKind.Interested,
            now,
            CancellationToken.None);
        await feedbackStore.SetAsync(
            article.Id,
            "actor-negative",
            ArticleFeedbackKind.NotInterested,
            now,
            CancellationToken.None);

        ArticleFeedbackAggregate aggregate = await feedbackStore.GetAggregateAsync(
            article.Id,
            CancellationToken.None);
        Assert.Equal(2, aggregate.FeedbackCount);
        Assert.Equal(1, aggregate.Weight);
        Assert.Equal(5m, aggregate.ScoreAdjustment);

        PostgresArticleRankingReader reader = new(dataSource);
        IReadOnlyList<RankedArticle> ranked = await reader.GetTopAsync(
            new ArticleRankingQuery(
                now.AddMinutes(-1),
                ArticleTopic.ArtificialIntelligence,
                100),
            CancellationToken.None);
        RankedArticle? result = FindArticle(ranked, article.Id);

        Assert.NotNull(result);
        Assert.Equal(84m, result.BaseScore);
        Assert.Equal(1, result.FeedbackWeight);
        Assert.Equal(89m, result.EffectiveScore);

        await feedbackStore.RemoveAsync(
            article.Id,
            "actor-negative",
            CancellationToken.None);
        aggregate = await feedbackStore.GetAggregateAsync(
            article.Id,
            CancellationToken.None);
        Assert.Equal(1, aggregate.FeedbackCount);
        Assert.Equal(3, aggregate.Weight);
    }

    [Fact]
    public async Task SearchAndTopExcludeOnlyTheActorsHiddenArticles()
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

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string searchToken = $"UnrealSignal{Guid.NewGuid():N}";
        Article article = Article.Create(
            $"{searchToken} tooling release",
            new Uri($"https://example.com/search/{Guid.NewGuid():N}"),
            "integration-search",
            now,
            now,
            externalId: Guid.NewGuid().ToString("N"),
            ArticleAssessment.Unclassified);
        PostgresArticleInbox inbox = new(dataSource);
        Assert.True(await inbox.TryAddAsync(article, CancellationToken.None));

        PostgresArticleFeedbackStore feedbackStore = new(dataSource);
        await feedbackStore.SetAsync(
            article.Id,
            "discord:42",
            ArticleFeedbackKind.Hidden,
            now,
            CancellationToken.None);

        PostgresArticleRankingReader reader = new(dataSource);
        IReadOnlyList<RankedArticle> hiddenSearch = await reader.SearchAsync(
            new ArticleSearchQuery(
                searchToken,
                now.AddMinutes(-1),
                ArticleTopic.None,
                25,
                "discord:42"),
            CancellationToken.None);
        IReadOnlyList<RankedArticle> visibleSearch = await reader.SearchAsync(
            new ArticleSearchQuery(
                searchToken,
                now.AddMinutes(-1),
                ArticleTopic.None,
                25,
                "discord:99"),
            CancellationToken.None);
        IReadOnlyList<RankedArticle> hiddenTop = await reader.GetTopAsync(
            new ArticleRankingQuery(
                now.AddMinutes(-1),
                ArticleTopic.None,
                100,
                "discord:42"),
            CancellationToken.None);

        Assert.Null(FindArticle(hiddenSearch, article.Id));
        Assert.NotNull(FindArticle(visibleSearch, article.Id));
        Assert.Null(FindArticle(hiddenTop, article.Id));
    }

    private static RankedArticle? FindArticle(
        IReadOnlyList<RankedArticle> articles,
        Guid articleId)
    {
        for (int index = 0; index < articles.Count; index++)
        {
            if (articles[index].ArticleId == articleId)
            {
                return articles[index];
            }
        }

        return null;
    }
}
