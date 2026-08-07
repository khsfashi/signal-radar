using SignalRadar.Application.Articles;
using SignalRadar.Application.Digests;
using SignalRadar.Domain.Articles;
using Xunit;

namespace SignalRadar.Application.Tests.Digests;

public sealed class GenerateArticleDigestUseCaseTests
{
    [Theory]
    [InlineData(ArticleDigestPeriod.Daily, 1)]
    [InlineData(ArticleDigestPeriod.Weekly, 7)]
    public async Task GenerateAsync_UsesExpectedWindowAndActor(
        ArticleDigestPeriod period,
        int expectedDays)
    {
        DateTimeOffset now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
        StubRankingReader reader = new();
        GenerateArticleDigestUseCase useCase = new(
            reader,
            new FixedTimeProvider(now));

        ArticleDigest digest = await useCase.GenerateAsync(
            "discord:123",
            period,
            ArticleTopic.ArtificialIntelligence,
            5,
            CancellationToken.None);

        Assert.Equal(now.AddDays(-expectedDays), digest.WindowStart);
        Assert.Equal(now, digest.WindowEnd);
        Assert.NotNull(reader.LastQuery);
        Assert.Equal("discord:123", reader.LastQuery.ActorId);
        Assert.Equal(5, reader.LastQuery.Limit);
        Assert.Equal(ArticleTopic.ArtificialIntelligence, reader.LastQuery.TopicMask);
    }

    private sealed class StubRankingReader : IArticleRankingReader
    {
        public ArticleRankingQuery? LastQuery { get; private set; }

        public ValueTask<IReadOnlyList<RankedArticle>> GetTopAsync(
            ArticleRankingQuery query,
            CancellationToken cancellationToken)
        {
            LastQuery = query;
            return ValueTask.FromResult<IReadOnlyList<RankedArticle>>([]);
        }

        public ValueTask<IReadOnlyList<RankedArticle>> SearchAsync(
            ArticleSearchQuery query,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
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
