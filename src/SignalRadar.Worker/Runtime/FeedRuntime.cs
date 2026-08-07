using Npgsql;
using SignalRadar.Application.Articles;
using SignalRadar.Application.Feeds;
using SignalRadar.Infrastructure.Feeds;
using SignalRadar.Worker.Feeds;

namespace SignalRadar.Worker.Runtime;

internal sealed class FeedRuntime : IDisposable
{
    private readonly HttpClient _httpClient;

    private FeedRuntime(HttpClient httpClient, Task runTask)
    {
        _httpClient = httpClient;
        RunTask = runTask;
    }

    public Task RunTask { get; }

    public static async ValueTask<FeedRuntime> StartAsync(
        NpgsqlDataSource dataSource,
        CollectArticleUseCase collectArticle,
        TimeProvider timeProvider,
        string configurationPath,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        FeedSourceConfigurationLoader configurationLoader = new();
        IReadOnlyList<FeedSourceDefinition> definitions =
            await configurationLoader.LoadAsync(
                configurationPath,
                cancellationToken).ConfigureAwait(false);
        PostgresFeedSourceStore sourceStore = new(dataSource);
        await sourceStore
            .UpsertDefinitionsAsync(definitions, cancellationToken)
            .ConfigureAwait(false);

        SocketsHttpHandler handler = WorkerHttp.CreateHandler(
            WorkerEnvironment.ParseInteger(
                "FEED_HTTP_MAX_CONNECTIONS_PER_SERVER",
                defaultValue: 8,
                minimum: 1,
                maximum: 32));
        HttpClient httpClient = new(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        try
        {
            FeedHttpOptions httpOptions = new(
                TimeSpan.FromSeconds(WorkerEnvironment.ParseInteger(
                    "FEED_HTTP_TIMEOUT_SECONDS",
                    defaultValue: 20,
                    minimum: 1,
                    maximum: 120)),
                WorkerEnvironment.ParseInteger(
                    "FEED_HTTP_MAX_RESPONSE_BYTES",
                    defaultValue: 4 * 1024 * 1024,
                    minimum: 1024,
                    maximum: 16 * 1024 * 1024),
                maximumAttempts: WorkerEnvironment.ParseInteger(
                    "FEED_HTTP_MAX_ATTEMPTS",
                    defaultValue: 3,
                    minimum: 1,
                    maximum: 5),
                maximumRedirects: WorkerEnvironment.ParseInteger(
                    "FEED_HTTP_MAX_REDIRECTS",
                    defaultValue: 5,
                    minimum: 0,
                    maximum: 10),
                allowPrivateNetworkTargets: WorkerEnvironment.ParseBoolean(
                    "FEED_HTTP_ALLOW_PRIVATE_NETWORKS",
                    defaultValue: false));
            HttpFeedDocumentFetcher fetcher = new(
                httpClient,
                httpOptions,
                timeProvider);
            RssAtomFeedParser parser = new();
            CollectFeedSourceUseCase collectFeedSource = new(
                sourceStore,
                fetcher,
                parser,
                collectArticle,
                timeProvider);
            FeedPollingLoop pollingLoop = new(
                sourceStore,
                collectFeedSource,
                timeProvider,
                WorkerEnvironment.ParseInteger(
                    "FEED_POLL_BATCH_SIZE",
                    defaultValue: 4,
                    minimum: 1,
                    maximum: 32),
                TimeSpan.FromSeconds(WorkerEnvironment.ParseInteger(
                    "FEED_LEASE_SECONDS",
                    defaultValue: 120,
                    minimum: 30,
                    maximum: 1800)),
                TimeSpan.FromSeconds(WorkerEnvironment.ParseInteger(
                    "FEED_IDLE_DELAY_SECONDS",
                    defaultValue: 30,
                    minimum: 1,
                    maximum: 300)),
                log);

            Task runTask = pollingLoop.RunAsync(cancellationToken);
            log($"Loaded {definitions.Count} RSS/Atom source definitions.");
            return new FeedRuntime(httpClient, runTask);
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
