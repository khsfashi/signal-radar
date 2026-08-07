using SignalRadar.Application.Articles;
using SignalRadar.Application.Summaries;
using SignalRadar.Domain.Articles;
using Xunit;

namespace SignalRadar.Application.Tests.Summaries;

public sealed class GenerateArticleSummaryUseCaseTests
{
    [Fact]
    public async Task GenerateAsync_ReusesCachedStructuredSummary()
    {
        DateTimeOffset now = new(2026, 8, 6, 11, 30, 0, TimeSpan.Zero);
        StubSummaryProvider provider = new();
        InMemorySummaryCache cache = new();
        GenerateArticleSummaryUseCase useCase = new(
            provider,
            cache,
            new FixedTimeProvider(now));
        SavedArticle article = CreateArticle(now);

        GeneratedArticleSummary first = await useCase.GenerateAsync(
            [article],
            "ko",
            CancellationToken.None);
        GeneratedArticleSummary second = await useCase.GenerateAsync(
            [article],
            "ko",
            CancellationToken.None);

        Assert.False(first.CacheHit);
        Assert.True(second.CacheHit);
        Assert.Equal(first.Summary.InputHash, second.Summary.InputHash);
        Assert.Equal(1, provider.CallCount);
        Assert.NotNull(provider.LastRequest);
        Assert.Contains("Do not claim that you read", provider.LastRequest.Instructions);
        Assert.Contains(article.CanonicalUrl.AbsoluteUri, provider.LastRequest.Input);
        Assert.Equal("ko", first.Summary.Language);
        Assert.Equal(now, first.Summary.GeneratedAt);
    }

    [Fact]
    public void InputHasher_ChangesForModelLanguageAndArticleOrder()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SavedArticle first = CreateArticle(now);
        SavedArticle second = first with
        {
            ArticleId = Guid.NewGuid(),
            Title = "Second article",
            CanonicalUrl = new Uri("https://example.com/second")
        };
        string baseline = ArticleSummaryInputHasher.Compute(
            [first, second],
            "provider",
            "model-a",
            GenerateArticleSummaryUseCase.PromptVersion,
            "ko");
        string differentModel = ArticleSummaryInputHasher.Compute(
            [first, second],
            "provider",
            "model-b",
            GenerateArticleSummaryUseCase.PromptVersion,
            "ko");
        string differentLanguage = ArticleSummaryInputHasher.Compute(
            [first, second],
            "provider",
            "model-a",
            GenerateArticleSummaryUseCase.PromptVersion,
            "en");
        string differentOrder = ArticleSummaryInputHasher.Compute(
            [second, first],
            "provider",
            "model-a",
            GenerateArticleSummaryUseCase.PromptVersion,
            "ko");

        Assert.Equal(64, baseline.Length);
        Assert.NotEqual(baseline, differentModel);
        Assert.NotEqual(baseline, differentLanguage);
        Assert.NotEqual(baseline, differentOrder);
    }

    [Fact]
    public async Task GenerateAsync_RejectsMoreThanTwentyArticles()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<SavedArticle> articles = new(21);

        for (int index = 0; index < 21; index++)
        {
            SavedArticle article = CreateArticle(now) with
            {
                ArticleId = Guid.NewGuid(),
                Title = $"Article {index}",
                CanonicalUrl = new Uri($"https://example.com/{index}")
            };
            articles.Add(article);
        }

        GenerateArticleSummaryUseCase useCase = new(
            new StubSummaryProvider(),
            new InMemorySummaryCache(),
            TimeProvider.System);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await useCase.GenerateAsync(
                articles,
                "ko",
                CancellationToken.None));
    }

    private static SavedArticle CreateArticle(DateTimeOffset now)
    {
        return new SavedArticle(
            Guid.NewGuid(),
            "OpenAI developer tool release",
            new Uri("https://example.com/openai-tool"),
            "official-source",
            now.AddHours(-2),
            now.AddMinutes(-10),
            ArticleTopic.ArtificialIntelligence | ArticleTopic.DeveloperTools,
            ArticleTopic.ArtificialIntelligence,
            80m,
            3,
            95m);
    }

    private sealed class StubSummaryProvider : IArticleSummaryProvider
    {
        public int CallCount { get; private set; }

        public ArticleSummaryProviderRequest? LastRequest { get; private set; }

        public string ProviderName => "stub-provider";

        public string ModelName => "stub-model";

        public ValueTask<ArticleSummaryProviderResponse> GenerateAsync(
            ArticleSummaryProviderRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            ArticleSummaryContent content = new(
                "AI tooling signal",
                "A saved title indicates an AI developer-tool release.",
                ["The source title combines AI and developer tooling."],
                "This may affect development workflows.",
                ["Verify the linked original."],
                ["Only metadata was supplied."]);
            return ValueTask.FromResult(
                new ArticleSummaryProviderResponse(content, "response-1"));
        }
    }

    private sealed class InMemorySummaryCache : IArticleSummaryCache
    {
        private readonly Dictionary<string, ArticleSummaryCacheEntry> _entries =
            new(StringComparer.Ordinal);

        public ValueTask<ArticleSummaryCacheEntry?> TryGetAsync(
            string inputHash,
            CancellationToken cancellationToken)
        {
            _entries.TryGetValue(inputHash, out ArticleSummaryCacheEntry? entry);
            return ValueTask.FromResult(entry);
        }

        public ValueTask StoreAsync(
            ArticleSummaryCacheEntry entry,
            CancellationToken cancellationToken)
        {
            _entries.TryAdd(entry.InputHash, entry);
            return ValueTask.CompletedTask;
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
