using SignalRadar.Application.Articles;
using SignalRadar.Domain.Articles;
using Xunit;

namespace SignalRadar.Application.Tests.Articles;

public sealed class CollectArticleUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsDuplicateWhenInboxRejectsArticle()
    {
        StubInbox inbox = new(wasAdded: false);
        StubCanonicalizer canonicalizer = new(new Uri("https://example.com/article"));
        ArticleAssessment assessment = new(
            ArticleTopic.ArtificialIntelligence,
            ArticleTopic.ArtificialIntelligence,
            90,
            100,
            70,
            100,
            91m,
            "test-v1");
        StubAssessmentPolicy assessmentPolicy = new(assessment);
        CollectArticleUseCase useCase = new(
            inbox,
            canonicalizer,
            assessmentPolicy,
            TimeProvider.System);

        CollectedArticleCandidate candidate = new(
            "Article",
            "https://example.com/article?utm_source=test",
            "source",
            DateTimeOffset.UtcNow);

        CollectArticleResult result = await useCase.ExecuteAsync(candidate);

        Assert.Equal(CollectArticleStatus.Duplicate, result.Status);
        Assert.Same(inbox.ReceivedArticle, result.Article);
        Assert.Same(assessment, result.Article.Assessment);
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

    private sealed class StubAssessmentPolicy : IArticleAssessmentPolicy
    {
        private readonly ArticleAssessment _assessment;

        public StubAssessmentPolicy(ArticleAssessment assessment)
        {
            _assessment = assessment;
        }

        public ArticleAssessment Assess(
            CollectedArticleCandidate candidate,
            Uri canonicalUrl,
            DateTimeOffset collectedAt)
        {
            return _assessment;
        }
    }
}
