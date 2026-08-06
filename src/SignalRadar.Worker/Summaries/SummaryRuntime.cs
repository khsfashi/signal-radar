using System.Net;
using Npgsql;
using SignalRadar.Application.Summaries;
using SignalRadar.Infrastructure.Summaries;

namespace SignalRadar.Worker.Summaries;

public sealed class SummaryRuntime : IDisposable
{
    private readonly HttpClient? _httpClient;

    private SummaryRuntime(
        GenerateArticleSummaryUseCase? useCase,
        HttpClient? httpClient,
        string? description)
    {
        UseCase = useCase;
        _httpClient = httpClient;
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
            return new SummaryRuntime(null, null, null);
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
        SocketsHttpHandler handler = new()
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            MaxConnectionsPerServer = ParseInteger(
                "SUMMARY_HTTP_MAX_CONNECTIONS_PER_SERVER",
                defaultValue: 2,
                minimum: 1,
                maximum: 8),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
        };
        HttpClient httpClient = new(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        try
        {
            OpenAiResponsesArticleSummaryProvider provider = new(
                httpClient,
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
            PostgresArticleSummaryCache cache = new(dataSource);
            GenerateArticleSummaryUseCase useCase = new(
                provider,
                cache,
                timeProvider);
            return new SummaryRuntime(
                useCase,
                httpClient,
                $"{provider.ProviderName}/{provider.ModelName}");
        }
        catch
        {
            httpClient.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
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
