using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SignalRadar.Application.Articles;
using SignalRadar.Application.Summaries;
using SignalRadar.Domain.Articles;
using SignalRadar.Infrastructure.Summaries;
using Xunit;

namespace SignalRadar.Infrastructure.Tests.Summaries;

public sealed class ArticleContentExtractionTests
{
    [Fact]
    public async Task GetAsync_ExtractsArticleTextAndReusesFreshCache()
    {
        DateTimeOffset now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
        RoutingHandler handler = new(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/robots.txt")
            {
                return TextResponse(
                    "User-agent: *\nAllow: /\n",
                    "text/plain");
            }

            string html = """
                <html>
                  <head><title>Useful release notes</title></head>
                  <body>
                    <nav>Home Products Pricing Sign in</nav>
                    <div class="cookie-banner">Accept all cookies</div>
                    <article>
                      <h1>Major engine tooling release</h1>
                      <p>The release introduces a deterministic build pipeline for large game projects and improves editor diagnostics for developers working across several platforms.</p>
                      <p>Teams can inspect build stages, compare cached artifacts, and identify expensive steps without changing the source project or relying on hidden configuration.</p>
                      <p>The article also explains migration considerations, compatibility boundaries, and the operational checks recommended before enabling the new pipeline in production.</p>
                    </article>
                    <script>ignoreThisSecret()</script>
                  </body>
                </html>
                """;
            return TextResponse(html, "text/html");
        });
        using HttpClient client = new(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        InMemoryContentCache cache = new();
        HttpArticleContentReader reader = new(
            client,
            cache,
            CreateOptions(),
            new FixedTimeProvider(now));
        SavedArticle article = CreateArticle("https://example.test/story");

        ArticleContentSnapshot first = await reader.GetAsync(
            article,
            CancellationToken.None);
        ArticleContentSnapshot second = await reader.GetAsync(
            article,
            CancellationToken.None);

        Assert.Equal(ArticleContentStatus.Extracted, first.Status);
        Assert.True(first.HasExtractedContent);
        Assert.Equal(64, first.ContentHash?.Length);
        Assert.Contains("deterministic build pipeline", first.Text);
        Assert.DoesNotContain("Accept all cookies", first.Text);
        Assert.DoesNotContain("ignoreThisSecret", first.Text);
        Assert.Equal(first, second);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task GetAsync_DoesNotFetchArticleWhenRobotsDisallowsPath()
    {
        RoutingHandler handler = new(request => TextResponse(
            "User-agent: SignalRadarBot\nDisallow: /private\n",
            "text/plain"));
        using HttpClient client = new(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        HttpArticleContentReader reader = new(
            client,
            new InMemoryContentCache(),
            CreateOptions(),
            TimeProvider.System);

        ArticleContentSnapshot result = await reader.GetAsync(
            CreateArticle("https://example.test/private/story"),
            CancellationToken.None);

        Assert.Equal(ArticleContentStatus.RobotsDisallowed, result.Status);
        Assert.False(result.HasExtractedContent);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetAsync_RejectsNonHtmlResponses()
    {
        RoutingHandler handler = new(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/robots.txt")
            {
                return TextResponse(string.Empty, "text/plain");
            }

            return TextResponse("not really a pdf", "application/pdf");
        });
        using HttpClient client = new(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        HttpArticleContentReader reader = new(
            client,
            new InMemoryContentCache(),
            CreateOptions(),
            TimeProvider.System);

        ArticleContentSnapshot result = await reader.GetAsync(
            CreateArticle("https://example.test/file.pdf"),
            CancellationToken.None);

        Assert.Equal(ArticleContentStatus.UnsupportedContentType, result.Status);
        Assert.Equal("application/pdf", result.ContentType);
    }

    [Fact]
    public void RobotsPolicy_UsesLongestMatchAndAllowWinsTies()
    {
        RobotsExclusionPolicy policy = RobotsExclusionPolicy.Parse("""
            User-agent: *
            Disallow: /docs/
            Allow: /docs/public/
            Disallow: /*.zip$
            Allow: /same
            Disallow: /same
            """);

        Assert.False(policy.IsAllowed(
            new Uri("https://example.test/docs/private/page"),
            "SignalRadarBot"));
        Assert.True(policy.IsAllowed(
            new Uri("https://example.test/docs/public/page"),
            "SignalRadarBot"));
        Assert.False(policy.IsAllowed(
            new Uri("https://example.test/files/archive.zip"),
            "SignalRadarBot"));
        Assert.True(policy.IsAllowed(
            new Uri("https://example.test/same"),
            "SignalRadarBot"));
    }

    private static ArticleContentHttpOptions CreateOptions()
    {
        return new ArticleContentHttpOptions(
            TimeSpan.FromSeconds(5),
            maximumResponseBytes: 64 * 1024,
            maximumRobotsBytes: 500 * 1024,
            minimumExtractedCharacters: 100,
            maximumExtractedCharacters: 5000,
            maximumRedirects: 5,
            allowPrivateNetworkTargets: true,
            successCacheDuration: TimeSpan.FromDays(7),
            unavailableCacheDuration: TimeSpan.FromHours(1),
            robotsCacheDuration: TimeSpan.FromDays(1));
    }

    private static SavedArticle CreateArticle(string url)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new SavedArticle(
            Guid.NewGuid(),
            "Article title",
            new Uri(url),
            "test-source",
            now.AddHours(-1),
            now,
            ArticleTopic.DeveloperTools,
            ArticleTopic.DeveloperTools,
            70m,
            0,
            70m);
    }

    private static HttpResponseMessage TextResponse(
        string content,
        string mediaType)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType)
        {
            CharSet = "utf-8"
        };
        return response;
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _route;
        private int _requestCount;

        public RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> route)
        {
            _route = route;
        }

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(_route(request));
        }
    }

    private sealed class InMemoryContentCache : IArticleContentCache
    {
        private readonly Dictionary<Guid, ArticleContentSnapshot> _entries = [];

        public ValueTask<ArticleContentSnapshot?> TryGetFreshAsync(
            Guid articleId,
            Uri canonicalUrl,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            if (_entries.TryGetValue(articleId, out ArticleContentSnapshot? entry)
                && entry.CanonicalUrl == canonicalUrl
                && entry.RefreshAfter > now)
            {
                return ValueTask.FromResult<ArticleContentSnapshot?>(entry);
            }

            return ValueTask.FromResult<ArticleContentSnapshot?>(null);
        }

        public ValueTask StoreAsync(
            ArticleContentSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            _entries[snapshot.ArticleId] = snapshot;
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
