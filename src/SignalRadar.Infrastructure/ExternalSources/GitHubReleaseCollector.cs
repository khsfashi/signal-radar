using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using SignalRadar.Application.ExternalSources;

namespace SignalRadar.Infrastructure.ExternalSources;

public sealed class GitHubReleaseCollector : IExternalSourceCollector
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly BoundedJsonHttpClient _httpClient;
    private readonly string? _token;

    public GitHubReleaseCollector(
        BoundedJsonHttpClient httpClient,
        string? token)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
    }

    public string SourceType => ExternalSourceTypes.GitHubReleases;

    public async ValueTask<ExternalFetchResult> FetchAsync(
        ExternalSourceLease source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        GitHubReleaseSettings settings =
            JsonSerializer.Deserialize<GitHubReleaseSettings>(
                source.SettingsJson,
                SerializerOptions)
            ?? new GitHubReleaseSettings(false);
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
                "GitHub Releases returned a non-array JSON document.");
        }

        List<ExternalArticleItem> items = new();

        foreach (JsonElement release in document.RootElement.EnumerateArray())
        {
            if (GetBoolean(release, "draft")
                || !settings.IncludePrereleases && GetBoolean(release, "prerelease"))
            {
                continue;
            }

            if (!release.TryGetProperty("id", out JsonElement idElement)
                || !idElement.TryGetInt64(out long id)
                || id <= 0)
            {
                continue;
            }

            string? url = GetString(release, "html_url");
            string? tagName = GetString(release, "tag_name");
            string? releaseName = GetString(release, "name");
            string? publishedText = GetString(release, "published_at")
                ?? GetString(release, "created_at");

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? releaseUrl)
                || releaseUrl.Scheme != Uri.UriSchemeHttps
                || string.IsNullOrWhiteSpace(tagName)
                || !DateTimeOffset.TryParse(
                    publishedText,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset publishedAt))
            {
                continue;
            }

            string name = string.IsNullOrWhiteSpace(releaseName)
                ? tagName
                : releaseName.Trim();
            items.Add(new ExternalArticleItem(
                id.ToString(CultureInfo.InvariantCulture),
                $"{source.DisplayName}: {name}",
                releaseUrl,
                publishedAt));
        }

        return ExternalFetchResult.Downloaded(
            items,
            response.ETag,
            response.LastModified);
    }

    private void ConfigureRequest(HttpRequestMessage request)
    {
        request.Headers.UserAgent.Add(
            new ProductInfoHeaderValue("signal-radar", "1.0"));
        request.Headers.TryAddWithoutValidation(
            "X-GitHub-Api-Version",
            "2022-11-28");
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        if (_token is not null)
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _token);
        }
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
}
