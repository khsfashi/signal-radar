using Xunit;
using SignalRadar.Infrastructure.Articles;

namespace SignalRadar.Infrastructure.Tests.Articles;

public sealed class CanonicalUrlNormalizerTests
{
    private readonly CanonicalUrlNormalizer _normalizer = new();

    [Fact]
    public void Normalize_RemovesTrackingFragmentAndDefaultPort()
    {
        Uri result = _normalizer.Normalize(
            "HTTPS://Example.COM:443/news?utm_source=discord&b=2&a=1#section");

        Assert.Equal("https://example.com/news?a=1&b=2", result.AbsoluteUri);
    }

    [Fact]
    public void Normalize_PreservesNonTrackingParameters()
    {
        Uri result = _normalizer.Normalize(
            "https://example.com/search?q=ai%20agent&lang=ko");

        Assert.Equal(
            "https://example.com/search?lang=ko&q=ai%20agent",
            result.AbsoluteUri);
    }

    [Fact]
    public async Task Inbox_RejectsSameCanonicalUrlTwice()
    {
        InMemoryArticleInbox inbox = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        SignalRadar.Domain.Articles.Article first =
            SignalRadar.Domain.Articles.Article.Create(
                "First",
                new Uri("https://example.com/article"),
                "source-a",
                now,
                now);

        SignalRadar.Domain.Articles.Article second =
            SignalRadar.Domain.Articles.Article.Create(
                "Second",
                new Uri("https://example.com/article"),
                "source-b",
                now,
                now);

        Assert.True(await inbox.TryAddAsync(first, CancellationToken.None));
        Assert.False(await inbox.TryAddAsync(second, CancellationToken.None));
        Assert.Equal(1, inbox.Count);
    }
}
