using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SignalRadar.Application.Feeds;
using SignalRadar.Infrastructure.Feeds;
using Xunit;

namespace SignalRadar.Infrastructure.Tests.Feeds;

public sealed class HttpFeedDocumentFetcherTests
{
    [Fact]
    public async Task FetchAsync_RetriesTransientStatusAndSendsValidators()
    {
        SequenceHandler handler = new(
            request =>
            {
                Assert.Contains(request.Headers.IfNoneMatch, value =>
                    value.Tag == "\"v1\"");
                Assert.NotNull(request.Headers.IfModifiedSince);
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                HttpResponseMessage response = new(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(
                        Encoding.UTF8.GetBytes("<rss><channel /></rss>"))
                };
                response.Headers.ETag = new EntityTagHeaderValue("\"v2\"");
                response.Content.Headers.LastModified =
                    new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero);
                return response;
            });
        using HttpClient client = new(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        FeedHttpOptions options = new(
            TimeSpan.FromSeconds(5),
            maximumResponseBytes: 1024 * 1024,
            maximumAttempts: 2,
            baseRetryDelay: TimeSpan.Zero,
            allowPrivateNetworkTargets: true);
        HttpFeedDocumentFetcher fetcher = new(
            client,
            options,
            TimeProvider.System);
        FeedSourceLease source = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "test",
            new Uri("https://feed.test/rss"),
            TimeSpan.FromMinutes(5),
            "\"v1\"",
            new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero),
            0);

        FeedFetchResult result = await fetcher.FetchAsync(
            source,
            CancellationToken.None);

        Assert.Equal(FeedFetchStatus.Downloaded, result.Status);
        Assert.Equal("\"v2\"", result.ETag);
        Assert.Equal(2, handler.RequestCount);
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>[] _responses;
        private int _requestCount;

        public SequenceHandler(
            params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
        {
            _responses = responses;
        }

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = Interlocked.Increment(ref _requestCount) - 1;

            if (index >= _responses.Length)
            {
                throw new InvalidOperationException("No response was configured.");
            }

            return Task.FromResult(_responses[index](request));
        }
    }
}
