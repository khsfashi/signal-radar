using Xunit;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Domain.Tests.Articles;

public sealed class ArticleTests
{
    [Fact]
    public void Create_TrimsTextAndNormalizesTimestampsToUtc()
    {
        DateTimeOffset publishedAt = new(2026, 8, 6, 9, 0, 0, TimeSpan.FromHours(9));
        DateTimeOffset collectedAt = new(2026, 8, 6, 9, 5, 0, TimeSpan.FromHours(9));

        Article article = Article.Create(
            "  New model released  ",
            new Uri("https://example.com/news"),
            "  official-blog  ",
            publishedAt,
            collectedAt);

        Assert.Equal("New model released", article.Title);
        Assert.Equal("official-blog", article.Source);
        Assert.Equal(TimeSpan.Zero, article.PublishedAt.Offset);
        Assert.Equal(TimeSpan.Zero, article.CollectedAt.Offset);
    }

    [Fact]
    public void Create_RejectsRelativeUrl()
    {
        Uri relativeUrl = new("/news", UriKind.Relative);

        Assert.Throws<ArgumentException>(
            () => Article.Create(
                "Title",
                relativeUrl,
                "source",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));
    }
}
