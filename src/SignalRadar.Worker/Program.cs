using System.Net;
using Npgsql;
using SignalRadar.Application.Articles;
using SignalRadar.Application.ExternalSources;
using SignalRadar.Application.Feeds;
using SignalRadar.Bot.Discord;
using SignalRadar.Infrastructure.Articles;
using SignalRadar.Infrastructure.Database;
using SignalRadar.Infrastructure.Discord;
using SignalRadar.Infrastructure.ExternalSources;
using SignalRadar.Infrastructure.Feeds;
using SignalRadar.Infrastructure.Ranking;
using SignalRadar.Worker.ExternalSources;
using SignalRadar.Worker.Feeds;

using CancellationTokenSource shutdown = new();
using CancellationTokenSource lifetime =
    CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

bool discordEnabled = ParseBoolean("DISCORD_ENABLED", defaultValue: true);
string? feedConfigurationPath = Environment.GetEnvironmentVariable(
    "FEED_SOURCE_CONFIG_PATH");
bool feedsEnabled = !string.IsNullOrWhiteSpace(feedConfigurationPath);
string? githubConfigurationPath = Environment.GetEnvironmentVariable(
    "GITHUB_RELEASE_SOURCE_CONFIG_PATH");
bool hackerNewsEnabled = ParseBoolean(
    "HACKER_NEWS_ENABLED",
    defaultValue: false);
bool externalSourcesEnabled =
    !string.IsNullOrWhiteSpace(githubConfigurationPath)
    || hackerNewsEnabled;

if (!discordEnabled && !feedsEnabled && !externalSourcesEnabled)
{
    throw new InvalidOperationException(
        "At least one ingestion pipeline must be enabled.");
}

TimeProvider timeProvider = TimeProvider.System;
string connectionString = GetRequiredEnvironmentVariable(
    "DATABASE_CONNECTION_STRING");
await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);

PostgresDatabaseMigrator migrator = new(dataSource);
await migrator.MigrateAsync(lifetime.Token);

PostgresHealthCheck healthCheck = new(dataSource);
await healthCheck.CheckAsync(lifetime.Token);
Console.WriteLine("PostgreSQL migrations and startup health check completed.");

ArticleRankingProfileLoader rankingProfileLoader = new();
ArticleRankingProfile rankingProfile = await rankingProfileLoader.LoadAsync(
    Environment.GetEnvironmentVariable("RANKING_PROFILE_PATH"),
    lifetime.Token);
RuleBasedArticleAssessmentPolicy assessmentPolicy = new(rankingProfile);
Console.WriteLine(
    $"Loaded article ranking profile '{rankingProfile.Version}'.");

CanonicalUrlNormalizer urlNormalizer = new();
PostgresArticleInbox articleInbox = new(dataSource);
CollectArticleUseCase collectArticle = new(
    articleInbox,
    urlNormalizer,
    assessmentPolicy,
    timeProvider);
List<Task> runningTasks = [];
DiscordInboxGateway? discordGateway = null;
HttpClient? feedHttpClient = null;
HttpClient? externalHttpClient = null;

try
{
    if (feedsEnabled)
    {
        string configuredFeedPath = feedConfigurationPath
            ?? throw new InvalidOperationException(
                "FEED_SOURCE_CONFIG_PATH was not configured.");
        FeedSourceConfigurationLoader configurationLoader = new();
        IReadOnlyList<FeedSourceDefinition> definitions =
            await configurationLoader.LoadAsync(
                configuredFeedPath,
                lifetime.Token);
        PostgresFeedSourceStore sourceStore = new(dataSource);
        await sourceStore
            .UpsertDefinitionsAsync(definitions, lifetime.Token)
            .ConfigureAwait(false);

        SocketsHttpHandler handler = CreateHttpHandler(
            ParseInteger(
                "FEED_HTTP_MAX_CONNECTIONS_PER_SERVER",
                defaultValue: 8,
                minimum: 1,
                maximum: 32));
        feedHttpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        FeedHttpOptions httpOptions = new(
            TimeSpan.FromSeconds(ParseInteger(
                "FEED_HTTP_TIMEOUT_SECONDS",
                defaultValue: 20,
                minimum: 1,
                maximum: 120)),
            ParseInteger(
                "FEED_HTTP_MAX_RESPONSE_BYTES",
                defaultValue: 4 * 1024 * 1024,
                minimum: 1024,
                maximum: 16 * 1024 * 1024),
            maximumAttempts: ParseInteger(
                "FEED_HTTP_MAX_ATTEMPTS",
                defaultValue: 3,
                minimum: 1,
                maximum: 5),
            maximumRedirects: ParseInteger(
                "FEED_HTTP_MAX_REDIRECTS",
                defaultValue: 5,
                minimum: 0,
                maximum: 10),
            allowPrivateNetworkTargets: ParseBoolean(
                "FEED_HTTP_ALLOW_PRIVATE_NETWORKS",
                defaultValue: false));
        HttpFeedDocumentFetcher fetcher = new(
            feedHttpClient,
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
            ParseInteger(
                "FEED_POLL_BATCH_SIZE",
                defaultValue: 4,
                minimum: 1,
                maximum: 32),
            TimeSpan.FromSeconds(ParseInteger(
                "FEED_LEASE_SECONDS",
                defaultValue: 120,
                minimum: 30,
                maximum: 1800)),
            TimeSpan.FromSeconds(ParseInteger(
                "FEED_IDLE_DELAY_SECONDS",
                defaultValue: 30,
                minimum: 1,
                maximum: 300)),
            Console.WriteLine);

        runningTasks.Add(pollingLoop.RunAsync(lifetime.Token));
        Console.WriteLine(
            $"Loaded {definitions.Count} RSS/Atom source definitions.");
    }

    if (externalSourcesEnabled)
    {
        List<ExternalSourceDefinition> definitions = [];

        if (!string.IsNullOrWhiteSpace(githubConfigurationPath))
        {
            GitHubReleaseSourceConfigurationLoader loader = new();
            IReadOnlyList<ExternalSourceDefinition> githubDefinitions =
                await loader.LoadAsync(
                    githubConfigurationPath,
                    lifetime.Token);
            definitions.AddRange(githubDefinitions);
        }

        if (hackerNewsEnabled)
        {
            definitions.Add(HackerNewsSourceDefinitionFactory.Create(
                Environment.GetEnvironmentVariable("HACKER_NEWS_STORY_LIST")
                    ?? "topstories",
                ParseInteger(
                    "HACKER_NEWS_MAX_ITEMS",
                    defaultValue: 50,
                    minimum: 1,
                    maximum: 100),
                ParseInteger(
                    "HACKER_NEWS_MIN_SCORE",
                    defaultValue: 10,
                    minimum: 0,
                    maximum: 100_000),
                TimeSpan.FromMinutes(ParseInteger(
                    "HACKER_NEWS_POLL_INTERVAL_MINUTES",
                    defaultValue: 5,
                    minimum: 1,
                    maximum: 1440))));
        }

        PostgresExternalSourceStore sourceStore = new(dataSource);
        await sourceStore
            .UpsertDefinitionsAsync(definitions, lifetime.Token)
            .ConfigureAwait(false);

        SocketsHttpHandler handler = CreateHttpHandler(
            ParseInteger(
                "EXTERNAL_HTTP_MAX_CONNECTIONS_PER_SERVER",
                defaultValue: 8,
                minimum: 1,
                maximum: 32));
        externalHttpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        BoundedJsonHttpClient jsonClient = new(
            externalHttpClient,
            timeProvider,
            TimeSpan.FromSeconds(ParseInteger(
                "EXTERNAL_HTTP_TIMEOUT_SECONDS",
                defaultValue: 20,
                minimum: 1,
                maximum: 120)),
            ParseInteger(
                "EXTERNAL_HTTP_MAX_RESPONSE_BYTES",
                defaultValue: 4 * 1024 * 1024,
                minimum: 1024,
                maximum: 16 * 1024 * 1024),
            ParseInteger(
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
                ParseInteger(
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
            ParseInteger(
                "EXTERNAL_POLL_BATCH_SIZE",
                defaultValue: 4,
                minimum: 1,
                maximum: 32),
            TimeSpan.FromSeconds(ParseInteger(
                "EXTERNAL_LEASE_SECONDS",
                defaultValue: 120,
                minimum: 30,
                maximum: 1800)),
            TimeSpan.FromSeconds(ParseInteger(
                "EXTERNAL_IDLE_DELAY_SECONDS",
                defaultValue: 30,
                minimum: 1,
                maximum: 300)),
            Console.WriteLine);

        runningTasks.Add(pollingLoop.RunAsync(lifetime.Token));
        Console.WriteLine(
            $"Loaded {definitions.Count} external API source definitions.");
    }

    if (discordEnabled)
    {
        DiscordInboxOptions discordOptions = new(
            GetRequiredEnvironmentVariable("DISCORD_BOT_TOKEN"),
            ParseSnowflakes("DISCORD_ALLOWED_GUILD_IDS", required: true),
            ParseSnowflakes("DISCORD_ALLOWED_CHANNEL_IDS", required: true),
            ParseSnowflakes("DISCORD_ALLOWED_AUTHOR_IDS", required: false),
            ParseBoolean(
                "DISCORD_REQUIRE_AUTOMATED_AUTHOR",
                defaultValue: true),
            Environment.GetEnvironmentVariable("DISCORD_SOURCE_NAME")
                ?? "discord");
        DiscordMessageAccessPolicy accessPolicy = new(discordOptions);
        DiscordMessageArticleCandidateFactory candidateFactory = new();
        PostgresDiscordMessageReceiptStore receiptStore = new(
            dataSource,
            TimeSpan.FromMinutes(5));
        DiscordInboxProcessor processor = new(
            discordOptions,
            accessPolicy,
            candidateFactory,
            receiptStore,
            collectArticle);
        DiscordSocketMessageMapper mapper = new();

        discordGateway = new DiscordInboxGateway(
            discordOptions,
            processor,
            mapper,
            Console.WriteLine);
        runningTasks.Add(discordGateway.RunAsync(lifetime.Token));
    }

    if (runningTasks.Count == 0)
    {
        throw new InvalidOperationException(
            "No ingestion task was created from the current configuration.");
    }

    Task firstCompleted = await Task.WhenAny(runningTasks).ConfigureAwait(false);
    lifetime.Cancel();

    try
    {
        await Task.WhenAll(runningTasks).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
    {
    }

    await firstCompleted.ConfigureAwait(false);
}
finally
{
    lifetime.Cancel();
    discordGateway?.Dispose();
    feedHttpClient?.Dispose();
    externalHttpClient?.Dispose();
}

static SocketsHttpHandler CreateHttpHandler(int maxConnectionsPerServer)
{
    return new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        MaxConnectionsPerServer = maxConnectionsPerServer,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
    };
}

static string GetRequiredEnvironmentVariable(string name)
{
    string? value = Environment.GetEnvironmentVariable(name);

    return !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException(
            $"Required environment variable '{name}' is missing.");
}

static ulong[] ParseSnowflakes(string name, bool required)
{
    string? value = Environment.GetEnvironmentVariable(name);

    if (string.IsNullOrWhiteSpace(value))
    {
        return required
            ? throw new InvalidOperationException(
                $"Required environment variable '{name}' is missing.")
            : [];
    }

    string[] segments = value.Split(
        ',',
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    ulong[] ids = new ulong[segments.Length];

    for (int index = 0; index < segments.Length; index++)
    {
        if (!ulong.TryParse(segments[index], out ulong id) || id == 0)
        {
            throw new InvalidOperationException(
                $"Environment variable '{name}' contains an invalid Discord snowflake.");
        }

        ids[index] = id;
    }

    return ids;
}

static bool ParseBoolean(string name, bool defaultValue)
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

static int ParseInteger(
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
