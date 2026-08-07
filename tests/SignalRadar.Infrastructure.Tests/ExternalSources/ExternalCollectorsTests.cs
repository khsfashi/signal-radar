using System.Net;
using System.Text;
using System.Text.Json;
using SignalRadar.Application.ExternalSources;
using SignalRadar.Infrastructure.ExternalSources;
using Xunit;

namespace SignalRadar.Infrastructure.Tests.ExternalSources;

public sealed class ExternalCollectorsTests
{
    [Fact]
    public async Task GitHubCollector_UsesReleaseIdAndSkipsDrafts()
    {
        const string json = """
            [
              {
                "id": 101,
                "tag_name": "v1.0.0",
                "name": "Version 1.0",
                "html_url": "https://github.com/example/project/releases/tag/v1.0.0",
                "draft": false,
                "prerelease": false,
                "published_at": "2026-08-06T00:00:00Z"
              },
              {
                "id": 102,
                "tag_name": "v2.0.0",
                "html_url": "https://github.com/example/project/releases/tag/v2.0.0",
                "draft": true,
                "prerelease": false,
                "published_at": "2026-08-06T00:00:00Z"
              }
            ]
            """;
        using HttpClient client = new(new RoutingHandler(_ => Json(json)))
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        BoundedJsonHttpClient jsonClient = CreateJsonClient(client);
        GitHubReleaseCollector collector = new(jsonClient, token: null);
        ExternalSourceLease source = CreateLease(
            ExternalSourceTypes.GitHubReleases,
            "github:example/project",
            "Example Project",
            new Uri("https://api.github.com/repos/example/project/releases"),
            JsonSerializer.Serialize(new GitHubReleaseSettings(false)));

        ExternalFetchResult result = await collector.FetchAsync(
            source,
            CancellationToken.None);

        ExternalArticleItem item = Assert.Single(result.Items);
        Assert.Equal("101", item.ExternalId);
        Assert.Equal("Example Project: Version 1.0", item.Title);
    }

    [Fact]
    public async Task HackerNewsCollector_FiltersByScoreAndKeepsStoryId()
    {
        RoutingHandler handler = new(request =>
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;

            return path switch
            {
                "/v0/topstories.json" => Json("[1,2]"),
                "/v0/item/1.json" => Json(
                    "{\"id\":1,\"type\":\"story\",\"title\":\"Useful item\","
                    + "\"url\":\"https://example.com/item\",\"time\":1785974400,\"score\":100}"),
                "/v0/item/2.json" => Json(
                    "{\"id\":2,\"type\":\"story\",\"title\":\"Low score\","
                    + "\"time\":1785974400,\"score\":1}"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        using HttpClient client = new(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        BoundedJsonHttpClient jsonClient = CreateJsonClient(client);
        HackerNewsCollector collector = new(jsonClient, maximumConcurrentItems: 1);
        ExternalSourceLease source = CreateLease(
            ExternalSourceTypes.HackerNews,
            "hacker-news:topstories",
            "Hacker News topstories",
            new Uri(
                "https://hacker-news.firebaseio.com/v0/topstories.json"),
            JsonSerializer.Serialize(new HackerNewsSettings(2, 10)));

        ExternalFetchResult result = await collector.FetchAsync(
            source,
            CancellationToken.None);

        ExternalArticleItem item = Assert.Single(result.Items);
        Assert.Equal("1", item.ExternalId);
        Assert.Equal("Useful item", item.Title);
        Assert.Equal("https://example.com/item", item.Url.AbsoluteUri);
    }

    private static BoundedJsonHttpClient CreateJsonClient(HttpClient client)
    {
        return new BoundedJsonHttpClient(
            client,
            TimeProvider.System,
            TimeSpan.FromSeconds(5),
            maximumResponseBytes: 1024 * 1024,
            maximumAttempts: 1);
    }

    private static ExternalSourceLease CreateLease(
        string type,
        string key,
        string name,
        Uri endpoint,
        string settingsJson)
    {
        return new ExternalSourceLease(
            Guid.NewGuid(),
            Guid.NewGuid(),
            type,
            key,
            name,
            endpoint,
            TimeSpan.FromMinutes(5),
            settingsJson,
            null,
            null,
            0);
    }

    private static HttpResponseMessage Json(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _route;

        public RoutingHandler(Func<HttpRequestMessage, HttpResponseMessage> route)
        {
            _route = route;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_route(request));
        }
    }
}
