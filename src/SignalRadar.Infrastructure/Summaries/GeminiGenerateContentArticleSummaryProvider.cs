using System.Buffers;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SignalRadar.Application.Summaries;

namespace SignalRadar.Infrastructure.Summaries;

public sealed class GeminiGenerateContentArticleSummaryProvider
    : IArticleSummaryProvider
{
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string _apiKey;
    private readonly TimeSpan _timeout;
    private readonly int _maximumResponseBytes;

    public GeminiGenerateContentArticleSummaryProvider(
        HttpClient httpClient,
        Uri endpoint,
        string apiKey,
        string modelName,
        TimeSpan timeout,
        int maximumResponseBytes)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        if (!_endpoint.IsAbsoluteUri
            || (_endpoint.Scheme != Uri.UriSchemeHttps
                && !(_endpoint.Scheme == Uri.UriSchemeHttp && _endpoint.IsLoopback)))
        {
            throw new ArgumentException(
                "The Gemini endpoint must use HTTPS, except for loopback tests.",
                nameof(endpoint));
        }

        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (maximumResponseBytes is < 1024 or > 4 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes));
        }

        _apiKey = apiKey.Trim();
        ModelName = modelName.Trim();
        _timeout = timeout;
        _maximumResponseBytes = maximumResponseBytes;
    }

    public string ProviderName => "gemini-generate-content";

    public string ModelName { get; }

    public async ValueTask<ArticleSummaryProviderResponse> GenerateAsync(
        ArticleSummaryProviderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        byte[] requestBody = CreateRequestBody(request);
        using HttpRequestMessage message = new(HttpMethod.Post, _endpoint);
        message.Headers.Add("x-goog-api-key", _apiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            "application/json"));
        message.Content = new ByteArrayContent(requestBody);
        message.Content.Headers.ContentType = new MediaTypeHeaderValue(
            "application/json")
        {
            CharSet = Encoding.UTF8.WebName
        };

        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using HttpResponseMessage response = await _httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        byte[] responseBody = await ReadBoundedAsync(
            response.Content,
            _maximumResponseBytes,
            timeout.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string errorText = Encoding.UTF8.GetString(responseBody);
            throw new HttpRequestException(
                $"Gemini generateContent request failed with HTTP {(int)response.StatusCode} "
                    + $"({response.StatusCode}): {Truncate(errorText, 1000)}",
                inner: null,
                response.StatusCode);
        }

        using JsonDocument document = JsonDocument.Parse(responseBody);
        JsonElement root = document.RootElement;
        string outputText = ExtractOutputText(root);
        SummaryResponseDto dto = JsonSerializer.Deserialize<SummaryResponseDto>(
            outputText)
            ?? throw new InvalidOperationException(
                "The Gemini structured summary payload was empty.");
        ArticleSummaryContent content = new(
            dto.Title ?? string.Empty,
            dto.Overview ?? string.Empty,
            dto.KeyPoints ?? [],
            dto.WhyItMatters ?? string.Empty,
            dto.WatchNext ?? [],
            dto.Caveats ?? []);
        string? responseId = root.TryGetProperty(
                "responseId",
                out JsonElement responseIdElement)
            && responseIdElement.ValueKind == JsonValueKind.String
                ? responseIdElement.GetString()
                : null;
        return new ArticleSummaryProviderResponse(content, responseId);
    }

    private byte[] CreateRequestBody(ArticleSummaryProviderRequest request)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("systemInstruction");
            writer.WriteStartArray("parts");
            writer.WriteStartObject();
            writer.WriteString("text", request.Instructions);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();

            writer.WriteStartArray("contents");
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteStartArray("parts");
            writer.WriteStartObject();
            writer.WriteString("text", request.Input);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WriteStartObject("generationConfig");
            writer.WriteStartObject("responseFormat");
            writer.WriteStartObject("text");
            writer.WriteString("mimeType", "application/json");
            WriteSummarySchema(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteSummarySchema(Utf8JsonWriter writer)
    {
        writer.WriteStartObject("schema");
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        writer.WriteStartObject("properties");
        WriteStringProperty(writer, "title");
        WriteStringProperty(writer, "overview");
        WriteStringArrayProperty(writer, "key_points", 1, 5);
        WriteStringProperty(writer, "why_it_matters");
        WriteStringArrayProperty(writer, "watch_next", 0, 5);
        WriteStringArrayProperty(writer, "caveats", 0, 5);
        writer.WriteEndObject();
        writer.WriteStartArray("required");
        writer.WriteStringValue("title");
        writer.WriteStringValue("overview");
        writer.WriteStringValue("key_points");
        writer.WriteStringValue("why_it_matters");
        writer.WriteStringValue("watch_next");
        writer.WriteStringValue("caveats");
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteStringProperty(Utf8JsonWriter writer, string name)
    {
        writer.WriteStartObject(name);
        writer.WriteString("type", "string");
        writer.WriteEndObject();
    }

    private static void WriteStringArrayProperty(
        Utf8JsonWriter writer,
        string name,
        int minimumItems,
        int maximumItems)
    {
        writer.WriteStartObject(name);
        writer.WriteString("type", "array");
        writer.WriteNumber("minItems", minimumItems);
        writer.WriteNumber("maxItems", maximumItems);
        writer.WriteStartObject("items");
        writer.WriteString("type", "string");
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static string ExtractOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out JsonElement candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                "The Gemini response did not contain a candidate.");
        }

        JsonElement candidate = candidates[0];

        if (candidate.TryGetProperty("finishReason", out JsonElement finishReason)
            && finishReason.ValueKind == JsonValueKind.String
            && string.Equals(
                finishReason.GetString(),
                "SAFETY",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Gemini blocked the structured summary for safety reasons.");
        }

        if (!candidate.TryGetProperty("content", out JsonElement content)
            || !content.TryGetProperty("parts", out JsonElement parts)
            || parts.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "The Gemini response did not contain content parts.");
        }

        foreach (JsonElement part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out JsonElement text)
                && text.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(text.GetString()))
            {
                return text.GetString()
                    ?? throw new InvalidOperationException(
                        "The Gemini response text was null.");
            }
        }

        throw new InvalidOperationException(
            "The Gemini response did not contain structured output text.");
    }

    private static async ValueTask<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        long? contentLength = content.Headers.ContentLength;

        if (contentLength.HasValue && contentLength.Value > maximumBytes)
        {
            throw new InvalidOperationException(
                "The summary provider response exceeded the configured size limit.");
        }

        await using Stream stream = await content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using MemoryStream output = new(
            contentLength.HasValue
                ? checked((int)contentLength.Value)
                : Math.Min(maximumBytes, 64 * 1024));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);

        try
        {
            int total = 0;

            while (true)
            {
                int read = await stream.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken).ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);

                if (total > maximumBytes)
                {
                    throw new InvalidOperationException(
                        "The summary provider response exceeded the configured size limit.");
                }

                await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken).ConfigureAwait(false);
            }

            return output.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string Truncate(string value, int maximumLength)
    {
        string normalized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }

    private sealed class SummaryResponseDto
    {
        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("overview")]
        public string? Overview { get; init; }

        [JsonPropertyName("key_points")]
        public string[]? KeyPoints { get; init; }

        [JsonPropertyName("why_it_matters")]
        public string? WhyItMatters { get; init; }

        [JsonPropertyName("watch_next")]
        public string[]? WatchNext { get; init; }

        [JsonPropertyName("caveats")]
        public string[]? Caveats { get; init; }
    }
}
