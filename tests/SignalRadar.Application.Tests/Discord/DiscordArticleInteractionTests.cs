using SignalRadar.Application.Articles;
using SignalRadar.Bot.Discord;
using SignalRadar.Domain.Articles;
using Xunit;

namespace SignalRadar.Application.Tests.Discord;

public sealed class DiscordArticleInteractionTests
{
    [Theory]
    [InlineData(ArticleFeedbackKind.Interested)]
    [InlineData(ArticleFeedbackKind.NotInterested)]
    [InlineData(ArticleFeedbackKind.Hidden)]
    public void FeedbackCustomId_RoundTrips(ArticleFeedbackKind kind)
    {
        Guid articleId = Guid.NewGuid();
        string customId = DiscordArticleInteractionCodec.CreateFeedbackCustomId(
            kind,
            articleId);

        bool parsed = DiscordArticleInteractionCodec.TryParseFeedbackCustomId(
            customId,
            out Guid parsedArticleId,
            out ArticleFeedbackKind parsedKind);

        Assert.True(parsed);
        Assert.Equal(articleId, parsedArticleId);
        Assert.Equal(kind, parsedKind);
        Assert.True(customId.Length <= 100);
    }

    [Theory]
    [InlineData(DiscordArticleSaveAction.Add)]
    [InlineData(DiscordArticleSaveAction.Remove)]
    public void SaveCustomId_RoundTrips(DiscordArticleSaveAction action)
    {
        Guid articleId = Guid.NewGuid();
        string customId = DiscordArticleInteractionCodec.CreateSaveCustomId(
            action,
            articleId);

        bool parsed = DiscordArticleInteractionCodec.TryParseSaveCustomId(
            customId,
            out Guid parsedArticleId,
            out DiscordArticleSaveAction parsedAction);

        Assert.True(parsed);
        Assert.Equal(articleId, parsedArticleId);
        Assert.Equal(action, parsedAction);
        Assert.True(customId.Length <= 100);
    }

    [Theory]
    [InlineData("all", ArticleTopic.None)]
    [InlineData("ai", ArticleTopic.ArtificialIntelligence)]
    [InlineData("game-development", ArticleTopic.GameDevelopment)]
    [InlineData("developer-tools", ArticleTopic.DeveloperTools)]
    public void ParseTopic_ReturnsExpectedMask(
        string value,
        ArticleTopic expected)
    {
        Assert.Equal(expected, DiscordArticleInteractionCodec.ParseTopic(value));
    }

    [Fact]
    public async Task GetTopAsync_PassesActorAndRelativeWindow()
    {
        DateTimeOffset now = new(2026, 8, 6, 10, 0, 0, TimeSpan.Zero);
        StubRankingReader reader = new();
        StubFeedbackStore feedbackStore = new();
        StubSaveStore saveStore = new();
        DiscordArticleInteractionService service = CreateService(
            reader,
            feedbackStore,
            saveStore,
            now);

        await service.GetTopAsync(
            userId: 123,
            days: 7,
            ArticleTopic.ArtificialIntelligence,
            limit: 4,
            CancellationToken.None);

        Assert.NotNull(reader.TopQuery);
        Assert.Equal(now.AddDays(-7), reader.TopQuery.Since);
        Assert.Equal(ArticleTopic.ArtificialIntelligence, reader.TopQuery.TopicMask);
        Assert.Equal(4, reader.TopQuery.Limit);
        Assert.Equal("discord:123", reader.TopQuery.ActorId);
    }

    [Fact]
    public async Task SetFeedbackAsync_UsesDiscordActorIdentity()
    {
        DateTimeOffset now = new(2026, 8, 6, 10, 0, 0, TimeSpan.Zero);
        StubRankingReader reader = new();
        StubFeedbackStore feedbackStore = new();
        StubSaveStore saveStore = new();
        DiscordArticleInteractionService service = CreateService(
            reader,
            feedbackStore,
            saveStore,
            now);
        Guid articleId = Guid.NewGuid();

        await service.SetFeedbackAsync(
            456,
            articleId,
            ArticleFeedbackKind.Hidden,
            CancellationToken.None);

        Assert.Equal(articleId, feedbackStore.ArticleId);
        Assert.Equal("discord:456", feedbackStore.ActorId);
        Assert.Equal(ArticleFeedbackKind.Hidden, feedbackStore.Kind);
        Assert.Equal(now, feedbackStore.OccurredAt);
    }

    [Fact]
    public async Task SaveAndSavedQueries_UseDiscordActorIdentity()
    {
        DateTimeOffset now = new(2026, 8, 6, 10, 0, 0, TimeSpan.Zero);
        StubRankingReader reader = new();
        StubFeedbackStore feedbackStore = new();
        StubSaveStore saveStore = new();
        DiscordArticleInteractionService service = CreateService(
            reader,
            feedbackStore,
            saveStore,
            now);
        Guid articleId = Guid.NewGuid();

        Assert.True(await service.SaveAsync(
            789,
            articleId,
            CancellationToken.None));
        await service.GetSavedAsync(
            789,
            days: 90,
            ArticleTopic.DeveloperTools,
            limit: 5,
            CancellationToken.None);

        Assert.Equal(articleId, saveStore.ArticleId);
        Assert.Equal("discord:789", saveStore.ActorId);
        Assert.Equal(now, saveStore.SavedAt);
        Assert.NotNull(saveStore.Query);
        Assert.Equal("discord:789", saveStore.Query.ActorId);
        Assert.Equal(now.AddDays(-90), saveStore.Query.SavedSince);
        Assert.Equal(ArticleTopic.DeveloperTools, saveStore.Query.TopicMask);
    }

    private static DiscordArticleInteractionService CreateService(
        StubRankingReader reader,
        StubFeedbackStore feedbackStore,
        StubSaveStore saveStore,
        DateTimeOffset now)
    {
        return new DiscordArticleInteractionService(
            reader,
            feedbackStore,
            saveStore,
            saveStore,
            new SavedArticleMarkdownExporter(),
            new FixedTimeProvider(now));
    }

    private sealed class StubRankingReader : IArticleRankingReader
    {
        public ArticleRankingQuery? TopQuery { get; private set; }

        public ArticleSearchQuery? SearchQuery { get; private set; }

        public ValueTask<IReadOnlyList<RankedArticle>> GetTopAsync(
            ArticleRankingQuery query,
            CancellationToken cancellationToken)
        {
            TopQuery = query;
            return ValueTask.FromResult<IReadOnlyList<RankedArticle>>([]);
        }

        public ValueTask<IReadOnlyList<RankedArticle>> SearchAsync(
            ArticleSearchQuery query,
            CancellationToken cancellationToken)
        {
            SearchQuery = query;
            return ValueTask.FromResult<IReadOnlyList<RankedArticle>>([]);
        }
    }

    private sealed class StubFeedbackStore : IArticleFeedbackStore
    {
        public Guid ArticleId { get; private set; }

        public string? ActorId { get; private set; }

        public ArticleFeedbackKind Kind { get; private set; }

        public DateTimeOffset OccurredAt { get; private set; }

        public ValueTask SetAsync(
            Guid articleId,
            string actorId,
            ArticleFeedbackKind kind,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken)
        {
            ArticleId = articleId;
            ActorId = actorId;
            Kind = kind;
            OccurredAt = occurredAt;
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(
            Guid articleId,
            string actorId,
            CancellationToken cancellationToken)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask<ArticleFeedbackAggregate> GetAggregateAsync(
            Guid articleId,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new ArticleFeedbackAggregate(0, 0));
        }
    }

    private sealed class StubSaveStore : IArticleSaveStore, IArticleSavedReader
    {
        public Guid ArticleId { get; private set; }

        public string? ActorId { get; private set; }

        public DateTimeOffset SavedAt { get; private set; }

        public SavedArticleQuery? Query { get; private set; }

        public ValueTask<bool> TryAddAsync(
            Guid articleId,
            string actorId,
            DateTimeOffset savedAt,
            CancellationToken cancellationToken)
        {
            ArticleId = articleId;
            ActorId = actorId;
            SavedAt = savedAt;
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> RemoveAsync(
            Guid articleId,
            string actorId,
            CancellationToken cancellationToken)
        {
            ArticleId = articleId;
            ActorId = actorId;
            return ValueTask.FromResult(true);
        }

        public ValueTask<IReadOnlyList<SavedArticle>> GetSavedAsync(
            SavedArticleQuery query,
            CancellationToken cancellationToken)
        {
            Query = query;
            return ValueTask.FromResult<IReadOnlyList<SavedArticle>>([]);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }
    }
}
