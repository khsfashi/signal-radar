using Xunit;
using SignalRadar.Application.Articles;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Application.Tests.Articles;

public sealed class CollectArticleUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsDuplicateWhenInboxRejectsArticle()
    {
        StubInbox inbox = new(wasAdded: false);
        StubCanonicalizer canonicalizer = new(new Uri("https://example.com/article"));
        CollectArticleUseCase useCase = new(
            inbox,
            canonicalizer,
            TimeProvider.System);

        CollectedArticleCandidate candidate = new(
            "Article",
            "https://example.com/article?utm_source=test",
            "source",
            DateTimeOffset.UtcNow);

        CollectArticleResult result = await useCase.ExecuteAsync(candidate);

        Assert.Equal(CollectArticleStatus.Duplicate, result.Status);
        Assert.Same(inbox.ReceivedArticle, result.Article);
    }

    private sealed class StubInbox : IArticleInbox
    {
        private readonly bool _wasAdded;

        public StubInbox(bool wasAdded)
        {
            _wasAdded = wasAdded;
        }

        public Article? ReceivedArticle { get; private set; }

        public ValueTask<bool> TryAddAsync(
            Article article,
            CancellationToken cancellationToken)
        {
            ReceivedArticle = article;
            return ValueTask.FromResult(_wasAdded);
        }
    }

    private sealed class StubCanonicalizer : IUrlCanonicalizer
    {
        private readonly Uri _normalizedUri;

        public StubCanonicalizer(Uri normalizedUri)
        {
            _normalizedUri = normalizedUri;
        }

        public Uri Normalize(string url)
        {
            return _normalizedUri;
        }
    }
}
