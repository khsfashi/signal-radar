using System.Security.Cryptography;
using System.Text;
using SignalRadar.Application.Articles;
using SignalRadar.Application.Summaries;
using SignalRadar.Domain.Articles;
using Xunit;

namespace SignalRadar.Application.Tests.Summaries;

public sealed class ArticleContentSummaryTests
{
    [Fact]
    public async Task GenerateAsync_IncludesUntrustedExtractedTextAndContentHash()
    {
        DateTimeOffset now = new(2026, 8, 6, 12, 30, 0, TimeSpan.Zero);
        SavedArticle article = CreateArticle(now);
        MutableContentReader contentReader = new(now, "The engine release improves build diagnostics and cache inspection.");
        StubSummaryProvider provider = new();
        GenerateArticleSummaryUseCase useCase = new(
            provider,
            new InMemorySummaryCache(),
            new FixedTimeProvider(now),
            contentReader,
            maximumContentConcurrency: 2,
            maximumContentCharactersPerArticle: 1000,
            maximumTotalContentCharacters: 5000);

        GeneratedArticleSummary result = await useCase.GenerateAsync(
            [article],
            "ko",
            CancellationToken.None);

        Assert.Equal(GenerateArticleSummaryUseCase.ContentPromptVersion, result.Summary.PromptVersion);
        Assert.NotNull(provider.LastRequest);
        Assert.Contains("untrusted source material", provider.LastRequest.Instructions);
        Assert.Contains("<article-content", provider.LastRequest.Input);
        Assert.Contains("improves build diagnostics", provider.LastRequest.Input);
        Assert.Contains(contentReader.CurrentHash, provider.LastRequest.Input);
    }

    [Fact]
    public async Task GenerateAsync_InvalidatesSummaryWhenExtractedContentChanges()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SavedArticle article = CreateArticle(now);
        MutableContentReader contentReader = new(now, "First extracted article body.");
        StubSummaryProvider provider = new();
        GenerateArticleSummaryUseCase useCase = new(
            provider,
            new InMemorySummaryCache(),
            new FixedTimeProvider(now),
            contentReader);

        GeneratedArticleSummary first = await useCase.GenerateAsync(
            [article],
            "en",
            CancellationToken.None);
        contentReader.SetText("Second extracted article body with changed facts.");
        GeneratedArticleSummary second = await useCase.GenerateAsync(
            [article],
            "en",
            CancellationToken.None);

        Assert.NotEqual(first.Summary.InputHash, second.Summary.InputHash);
        Assert.Equal(2, provider.CallCount);
    }

    private static SavedArticle CreateArticle(DateTimeOffset now)
    {
        return new SavedArticle(
            Guid.NewGuid(),
            "Engine tooling release",
            new Uri("https://example.com/engine-tooling"),
            "official-source",
            now.AddHours(-2),
            now.AddMinutes(-5),
            ArticleTopic.GameDevelopment | ArticleTopic.DeveloperTools,
            ArticleTopic.GameDevelopment,
            80m,
            0,
            80m);
    }

    private sealed class MutableContentReader : IArticleContentReader
    {
        private readonly DateTimeOffset _now;
        private string _text;

        public MutableContentReader(DateTimeOffset now, string text)
        {
            _now = now;
            _text = text;
        }

        public string CurrentHash => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(_text)));

        public void SetText(string text)
        {
            _text = text;
        }

        public ValueTask<ArticleContentSnapshot> GetAsync(
            SavedArticle article,
            CancellationToken cancellationToken)
        {
            ArticleContentSnapshot snapshot = new(
                article.ArticleId,
                article.CanonicalUrl,
                ArticleContentStatus.Extracted,
                _text,
                CurrentHash,
                _now,
                _now.AddDays(1),
                200,
                "text/html",
                "Extracted for test.");
            return ValueTask.FromResult(snapshot);
        }
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
                "Tooling briefing",
                "The supplied material describes a tooling signal.",
                ["A build-tooling change is described."],
                "It may affect development workflows.",
                ["Verify the original source."],
                ["Extraction may be incomplete."]);
            return ValueTask.FromResult(new ArticleSummaryProviderResponse(content));
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
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }
    }
}
