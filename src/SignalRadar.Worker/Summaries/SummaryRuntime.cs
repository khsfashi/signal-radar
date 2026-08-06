using System.Net;
using Npgsql;
using SignalRadar.Application.Summaries;
using SignalRadar.Infrastructure.Summaries;

namespace SignalRadar.Worker.Summaries;

public sealed class SummaryRuntime : IDisposable
{
    private readonly HttpClient? _providerHttpClient;
    private readonly HttpClient? _contentHttpClient;

    private SummaryRuntime(
        GenerateArticleSummaryUseCase? useCase,
        HttpClient? providerHttpClient,
        HttpClient? contentHttpClient,
        string? description)
    {
        UseCase = useCase;
        _providerHttpClient = providerHttpClient;
        _contentHttpClient = contentHttpClient;
        Description = description;
    }

    public GenerateArticleSummaryUseCase? UseCase { get; }

    public string? Description { get; }

    public bool Enabled => UseCase is not null;

    public static SummaryRuntime Create(
        NpgsqlDataSource dataSource,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(timeProvider);
        string? configuredProvider = Environment.GetEnvironmentVariable(
            "SUMMARY_PROVIDER");

        if (string.IsNullOrWhiteSpace(configuredProvider)
            || string.Equals(
                configuredProvider.Trim(),
                "disabled",
                StringComparison.OrdinalIgnoreCase))
        {
            return new SummaryRuntime(null, null, null, null);
        }

        string providerName = configuredProvider.Trim().ToLowerInvariant();

        if (providerName != "openai-responses")
        {
            throw new InvalidOperationException(
                "SUMMARY_PROVIDER currently supports only 'openai-responses' or 'disabled'.");
        }

        string apiKey = GetRequiredEnvironmentVariable("OPENAI_API_KEY");
        string model = GetRequiredEnvironmentVariable("OPENAI_SUMMARY_MODEL");
        Uri endpoint = ParseEndpoint(
            Environment.GetEnvironmentVariable("OPENAI_RESPONSES_ENDPOINT")
                ?? "https://api.openai.com/v1/responses");
        HttpClient providerHttpClient = CreateHttpClient(
            ParseInteger(
                "SUMMARY_HTTP_MAX_CONNECTIONS_PER_SERVER",
                defaultValue: 2,
                minimum: 1,
                maximum: 8));
        HttpClient contentHttpClient = CreateHttpClient(
            ParseInteger(
                "ARTICLE_CONTENT_HTTP_MAX_CONNECTIONS_PER_SERVER",
                defaultValue: 4,
                minimum: 1,
                maximum: 8));

        try
        {
            OpenAiResponsesArticleSummaryProvider provider = new(
                providerHttpClient,
                endpoint,
                apiKey,
                model,
                TimeSpan.FromSeconds(ParseInteger(
                    "SUMMARY_HTTP_TIMEOUT_SECONDS",
                    defaultValue: 60,
                    minimum: 5,
                    maximum: 300)),
                ParseInteger(
                    "SUMMARY_HTTP_MAX_RESPONSE_BYTES",
                    defaultValue: 256 * 1024,
                    minimum: 1024,
                    maximum: 4 * 1024 * 1024));
            PostgresArticleSummaryCache summaryCache = new(dataSource);
            PostgresArticleContentCache contentCache = new(dataSource);
            ArticleContentHttpOptions contentOptions = new(
                TimeSpan.FromSeconds(ParseInteger(
                    "ARTICLE_CONTENT_HTTP_TIMEOUT_SECONDS",
                    defaultValue: 15,
                    minimum: 1,
                    maximum: 120)),
                ParseInteger(
                    "ARTICLE_CONTENT_HTTP_MAX_RESPONSE_BYTES",
                    defaultValue: 2 * 1024 * 1024,
                    minimum: 64 * 1024,
                    maximum: 8 * 1024 * 1024),
                ParseInteger(
                    "ARTICLE_CONTENT_ROBOTS_MAX_BYTES",
                    defaultValue: 512 * 1024,
                    minimum: 500 * 1024,
                    maximum: 2 * 1024 * 1024),
                ParseInteger(
                    "ARTICLE_CONTENT_MIN_EXTRACTED_CHARS",
                    defaultValue: 300,
                    minimum: 100,
                    maximum: 10_000),
                ParseInteger(
                    "ARTICLE_CONTENT_MAX_EXTRACTED_CHARS",
                    defaultValue: 30_000,
                    minimum: 1_000,
                    maximum: 100_000),
                ParseInteger(
                    "ARTICLE_CONTENT_MAX_REDIRECTS",
                    defaultValue: 5,
                    minimum: 0,
                    maximum: 10),
                ParseBoolean(
                    "ARTICLE_CONTENT_ALLOW_PRIVATE_NETWORKS",
                    defaultValue: false),
                TimeSpan.FromHours(ParseInteger(
                    "ARTICLE_CONTENT_SUCCESS_CACHE_HOURS",
                    defaultValue: 168,
                    minimum: 1,
                    maximum: 720)),
                TimeSpan.FromMinutes(ParseInteger(
                    "ARTICLE_CONTENT_FAILURE_CACHE_MINUTES",
                    defaultValue: 60,
                    minimum: 5,
                    maximum: 1440)),
                TimeSpan.FromMinutes(ParseInteger(
                    "ARTICLE_CONTENT_ROBOTS_CACHE_MINUTES",
                    defaultValue: 1440,
                    minimum: 5,
                    maximum: 1440)));
            HttpArticleContentReader contentReader = new(
                contentHttpClient,
                contentCache,
                contentOptions,
                timeProvider);
            GenerateArticleSummaryUseCase useCase = new(
                provider,
                summaryCache,
                timeProvider,
                contentReader,
                ParseInteger(
                    "SUMMARY_CONTENT_CONCURRENCY",
                    defaultValue: 4,
                    minimum: 1,
                    maximum: 8),
                ParseInteger(
                    "SUMMARY_CONTENT_CHARS_PER_ARTICLE",
                    defaultValue: 10_000,
                    minimum: 1_000,
                    maximum: 30_000),
                ParseInteger(
                    "SUMMARY_CONTENT_TOTAL_CHARS",
                    defaultValue: 60_000,
                    minimum: 5_000,
                    maximum: 200_000));
            return new SummaryRuntime(
                useCase,
                providerHttpClient,
                contentHttpClient,
                $"{provider.ProviderName}/{provider.ModelName} with article extraction");
        }
        catch
        {
            providerHttpClient.Dispose();
            contentHttpClient.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _providerHttpClient?.Dispose();
        _contentHttpClient?.Dispose();
    }

    private static HttpClient CreateHttpClient(int maximumConnectionsPerServer)
    {
        SocketsHttpHandler handler = new()
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            MaxConnectionsPerServer = maximumConnectionsPerServer,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
        };
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private static Uri ParseEndpoint(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? endpoint)
            ? endpoint
            : throw new InvalidOperationException(
                "OPENAI_RESPONSES_ENDPOINT must be an absolute URI.");
    }

    private static string GetRequiredEnvironmentVariable(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);

        return !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new InvalidOperationException(
                $"Required environment variable '{name}' is missing.");
    }

    private static bool ParseBoolean(string name, bool defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return bool.TryParse(value, out bool parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Environment variable '{name}' must be 'true' or 'false'.");
    }

    private static int ParseInteger(
        string name,
        int defaultValue,
        int minimum,
        int maximum)
    {
        string? value = Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, out int parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            throw new InvalidOperationException(
                $"Environment variable '{name}' must be between {minimum} and {maximum}.");
        }

        return parsed;
    }
}
