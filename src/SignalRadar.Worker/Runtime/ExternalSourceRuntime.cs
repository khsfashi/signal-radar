using Npgsql;
using SignalRadar.Application.Articles;
using SignalRadar.Application.ExternalSources;
using SignalRadar.Infrastructure.ExternalSources;
using SignalRadar.Worker.ExternalSources;

namespace SignalRadar.Worker.Runtime;

internal sealed class ExternalSourceRuntime : IDisposable
{
    private readonly HttpClient _httpClient;

    private ExternalSourceRuntime(HttpClient httpClient, Task runTask)
    {
        _httpClient = httpClient;
        RunTask = runTask;
    }

    public Task RunTask { get; }

    public static async ValueTask<ExternalSourceRuntime> StartAsync(
        NpgsqlDataSource dataSource,
        CollectArticleUseCase collectArticle,
        TimeProvider timeProvider,
        string? githubConfigurationPath,
        bool hackerNewsEnabled,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        List<ExternalSourceDefinition> definitions = [];

        if (!string.IsNullOrWhiteSpace(githubConfigurationPath))
        {
            GitHubReleaseSourceConfigurationLoader loader = new();
            IReadOnlyList<ExternalSourceDefinition> githubDefinitions =
                await loader.LoadAsync(
                    githubConfigurationPath,
                    cancellationToken).ConfigureAwait(false);
            definitions.AddRange(githubDefinitions);
        }

        if (hackerNewsEnabled)
        {
            definitions.Add(HackerNewsSourceDefinitionFactory.Create(
                Environment.GetEnvironmentVariable("HACKER_NEWS_STORY_LIST")
                    ?? "topstories",
                WorkerEnvironment.ParseInteger(
                    "HACKER_NEWS_MAX_ITEMS",
                    defaultValue: 50,
                    minimum: 1,
                    maximum: 100),
                WorkerEnvironment.ParseInteger(
                    "HACKER_NEWS_MIN_SCORE",
                    defaultValue: 10,
                    minimum: 0,
                    maximum: 100_000),
                TimeSpan.FromMinutes(WorkerEnvironment.ParseInteger(
                    "HACKER_NEWS_POLL_INTERVAL_MINUTES",
                    defaultValue: 5,
                    minimum: 1,
                    maximum: 1440))));
        }

        if (definitions.Count == 0)
        {
            throw new InvalidOperationException(
                "External source runtime requires at least one configured source.");
        }

        PostgresExternalSourceStore sourceStore = new(dataSource);
        await sourceStore
            .UpsertDefinitionsAsync(definitions, cancellationToken)
            .ConfigureAwait(false);

        SocketsHttpHandler handler = WorkerHttp.CreateHandler(
            WorkerEnvironment.ParseInteger(
                "EXTERNAL_HTTP_MAX_CONNECTIONS_PER_SERVER",
                defaultValue: 8,
                minimum: 1,
                maximum: 32));
        HttpClient httpClient = new(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        try
        {
            BoundedJsonHttpClient jsonClient = new(
                httpClient,
                timeProvider,
                TimeSpan.FromSeconds(WorkerEnvironment.ParseInteger(
                    "EXTERNAL_HTTP_TIMEOUT_SECONDS",
                    defaultValue: 20,
                    minimum: 1,
                    maximum: 120)),
                WorkerEnvironment.ParseInteger(
                    "EXTERNAL_HTTP_MAX_RESPONSE_BYTES",
                    defaultValue: 4 * 1024 * 1024,
                    minimum: 1024,
                    maximum: 16 * 1024 * 1024),
                WorkerEnvironment.ParseInteger(
                    "EXTERNAL_HTTP_MAX_ATTEMPTS",
                    defaultValue: 3,
                    minimum: 1,
                    maximum: 5));
            List<IExternalSourceCollector> collectors =
            [
                new GitHubReleaseCollector(
                    jsonClient,
                    Environment.GetEnvironmentVariable("GITHUB_API_TOKEN")),
                new HackerNewsCollector(
                    jsonClient,
                    WorkerEnvironment.ParseInteger(
                        "HACKER_NEWS_ITEM_CONCURRENCY",
                        defaultValue: 8,
                        minimum: 1,
                        maximum: 16))
            ];
            CollectExternalSourceUseCase collectExternalSource = new(
                sourceStore,
                collectors,
                collectArticle,
                timeProvider);
            ExternalSourcePollingLoop pollingLoop = new(
                sourceStore,
                collectExternalSource,
                timeProvider,
                WorkerEnvironment.ParseInteger(
                    "EXTERNAL_POLL_BATCH_SIZE",
                    defaultValue: 4,
                    minimum: 1,
                    maximum: 32),
                TimeSpan.FromSeconds(WorkerEnvironment.ParseInteger(
                    "EXTERNAL_LEASE_SECONDS",
                    defaultValue: 120,
                    minimum: 30,
                    maximum: 1800)),
                TimeSpan.FromSeconds(WorkerEnvironment.ParseInteger(
                    "EXTERNAL_IDLE_DELAY_SECONDS",
                    defaultValue: 30,
                    minimum: 1,
                    maximum: 300)),
                log);

            Task runTask = pollingLoop.RunAsync(cancellationToken);
            log($"Loaded {definitions.Count} external API source definitions.");
            return new ExternalSourceRuntime(httpClient, runTask);
        }
        catch
        {
            httpClient.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
