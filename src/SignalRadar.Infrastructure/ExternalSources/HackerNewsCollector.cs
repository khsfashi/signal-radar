using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using SignalRadar.Application.ExternalSources;

namespace SignalRadar.Infrastructure.ExternalSources;

public sealed record HackerNewsSettings(int MaximumItems, int MinimumScore);

public static class HackerNewsSourceDefinitionFactory
{
    private static readonly HashSet<string> SupportedLists =
        new(StringComparer.Ordinal)
        {
            "topstories",
            "beststories",
            "newstories"
        };

    public static ExternalSourceDefinition Create(
        string storyList,
        int maximumItems,
        int minimumScore,
        TimeSpan pollingInterval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storyList);

        if (!SupportedLists.Contains(storyList))
        {
            throw new ArgumentOutOfRangeException(nameof(storyList));
        }

        if (maximumItems is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumItems));
        }

        if (minimumScore is < 0 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumScore));
        }

        string settingsJson = JsonSerializer.Serialize(
            new HackerNewsSettings(maximumItems, minimumScore));

        return new ExternalSourceDefinition(
            ExternalSourceTypes.HackerNews,
            $"hacker-news:{storyList}",
            $"Hacker News {storyList}",
            new Uri(
                $"https://hacker-news.firebaseio.com/v0/{storyList}.json"),
            pollingInterval,
            settingsJson);
    }
}

public sealed class HackerNewsCollector : IExternalSourceCollector
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly BoundedJsonHttpClient _httpClient;
    private readonly int _maximumConcurrentItems;

    public HackerNewsCollector(
        BoundedJsonHttpClient httpClient,
        int maximumConcurrentItems)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        if (maximumConcurrentItems is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentItems));
        }

        _maximumConcurrentItems = maximumConcurrentItems;
    }

    public string SourceType => ExternalSourceTypes.HackerNews;

    public async ValueTask<ExternalFetchResult> FetchAsync(
        ExternalSourceLease source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        HackerNewsSettings settings = JsonSerializer.Deserialize<HackerNewsSettings>(
            source.SettingsJson,
            SerializerOptions)
            ?? throw new InvalidDataException(
                "Hacker News source settings are missing.");
        JsonHttpResult response = await _httpClient
            .GetAsync(
                source.Endpoint,
                source.ETag,
                source.LastModified,
                ConfigureRequest,
                cancellationToken)
            .ConfigureAwait(false);

        if (response.NotModified)
        {
            return ExternalFetchResult.Unchanged(
                response.ETag,
                response.LastModified);
        }

        using JsonDocument document = JsonDocument.Parse(response.Content);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "Hacker News returned a non-array story list.");
        }

        long[] storyIds = document.RootElement
            .EnumerateArray()
            .Where(static element =>
                element.ValueKind == JsonValueKind.Number
                && element.TryGetInt64(out long id)
                && id > 0)
            .Take(settings.MaximumItems)
            .Select(static element => element.GetInt64())
            .ToArray();
        using SemaphoreSlim concurrency = new(_maximumConcurrentItems);
        Task<ExternalArticleItem?>[] tasks = new Task<ExternalArticleItem?>[storyIds.Length];

        for (int index = 0; index < storyIds.Length; index++)
        {
            tasks[index] = FetchStoryAsync(
                storyIds[index],
                settings.MinimumScore,
                concurrency,
                cancellationToken);
        }

        ExternalArticleItem?[] results = await Task.WhenAll(tasks)
            .ConfigureAwait(false);
        List<ExternalArticleItem> items = new(results.Length);

        for (int index = 0; index < results.Length; index++)
        {
            if (results[index] is ExternalArticleItem item)
            {
                items.Add(item);
            }
        }

        return ExternalFetchResult.Downloaded(
            items,
            response.ETag,
            response.LastModified);
    }

    private async Task<ExternalArticleItem?> FetchStoryAsync(
        long storyId,
        int minimumScore,
        SemaphoreSlim concurrency,
        CancellationToken cancellationToken)
    {
        await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Uri endpoint = new(
                $"https://hacker-news.firebaseio.com/v0/item/{storyId}.json");
            JsonHttpResult response = await _httpClient
                .GetAsync(
                    endpoint,
                    null,
                    null,
                    ConfigureRequest,
                    cancellationToken)
                .ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(response.Content);
            JsonElement story = document.RootElement;

            if (story.ValueKind != JsonValueKind.Object
                || GetBoolean(story, "deleted")
                || GetBoolean(story, "dead")
                || !string.Equals(
                    GetString(story, "type"),
                    "story",
                    StringComparison.Ordinal)
                || !TryGetInt64(story, "id", out long id)
                || !TryGetInt64(story, "time", out long unixTime)
                || !TryGetInt64(story, "score", out long score)
                || score < minimumScore)
            {
                return null;
            }

            string? title = GetString(story, "title");

            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            string? articleUrl = GetString(story, "url");
            Uri url = Uri.TryCreate(articleUrl, UriKind.Absolute, out Uri? parsedUrl)
                && parsedUrl.Scheme is "http" or "https"
                    ? parsedUrl
                    : new Uri($"https://news.ycombinator.com/item?id={id}");

            return new ExternalArticleItem(
                id.ToString(CultureInfo.InvariantCulture),
                title.Trim(),
                url,
                DateTimeOffset.FromUnixTimeSeconds(unixTime));
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            concurrency.Release();
        }
    }

    private static void ConfigureRequest(HttpRequestMessage request)
    {
        request.Headers.UserAgent.Add(
            new ProductInfoHeaderValue("signal-radar", "1.0"));
    }

    private static bool GetBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.True;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static bool TryGetInt64(
        JsonElement element,
        string propertyName,
        out long value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out JsonElement property)
            && property.TryGetInt64(out value);
    }
}
