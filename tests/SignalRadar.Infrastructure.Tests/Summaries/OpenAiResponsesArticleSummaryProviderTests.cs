using System.Net;
using System.Text;
using System.Text.Json;
using SignalRadar.Application.Summaries;
using SignalRadar.Infrastructure.Summaries;
using Xunit;

namespace SignalRadar.Infrastructure.Tests.Summaries;

public sealed class OpenAiResponsesArticleSummaryProviderTests
{
    [Fact]
    public async Task GenerateAsync_SendsStrictSchemaAndParsesOutputText()
    {
        const string responseJson = """
            {
              "id": "resp_123",
              "output": [
                {
                  "type": "message",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "{\"title\":\"Signal briefing\",\"overview\":\"Overview\",\"key_points\":[\"Point one\"],\"why_it_matters\":\"Impact\",\"watch_next\":[\"Verify source\"],\"caveats\":[\"Metadata only\"]}"
                    }
                  ]
                }
              ]
            }
            """;
        StubHandler handler = new(responseJson);
        using HttpClient httpClient = new(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        OpenAiResponsesArticleSummaryProvider provider = new(
            httpClient,
            new Uri("https://api.openai.test/v1/responses"),
            "secret-key",
            "summary-model",
            TimeSpan.FromSeconds(30),
            64 * 1024);
        ArticleSummaryProviderRequest request = new(
            "Use metadata only.",
            "Title: Example",
            "ko");

        ArticleSummaryProviderResponse result = await provider.GenerateAsync(
            request,
            CancellationToken.None);

        Assert.Equal("resp_123", result.ProviderResponseId);
        Assert.Equal("Signal briefing", result.Content.Title);
        Assert.Equal(["Point one"], result.Content.KeyPoints);
        Assert.NotNull(handler.RequestBody);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("secret-key", handler.AuthorizationParameter);

        using JsonDocument document = JsonDocument.Parse(handler.RequestBody);
        JsonElement root = document.RootElement;
        Assert.Equal("summary-model", root.GetProperty("model").GetString());
        Assert.Equal("Use metadata only.", root.GetProperty("instructions").GetString());
        JsonElement format = root
            .GetProperty("text")
            .GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        Assert.False(format
            .GetProperty("schema")
            .GetProperty("additionalProperties")
            .GetBoolean());
    }

    [Fact]
    public async Task GenerateAsync_RejectsOversizedResponse()
    {
        StubHandler handler = new(new string('x', 4096));
        using HttpClient httpClient = new(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        OpenAiResponsesArticleSummaryProvider provider = new(
            httpClient,
            new Uri("https://api.openai.test/v1/responses"),
            "secret-key",
            "summary-model",
            TimeSpan.FromSeconds(30),
            1024);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.GenerateAsync(
                new ArticleSummaryProviderRequest("instructions", "input", "en"),
                CancellationToken.None));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public StubHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        public byte[]? RequestBody { get; private set; }

        public string? AuthorizationScheme { get; private set; }

        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    _responseBody,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
