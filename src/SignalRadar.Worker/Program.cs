using System.Net;
using Npgsql;
using SignalRadar.Application.Articles;
using SignalRadar.Application.Digests;
using SignalRadar.Application.ExternalSources;
using SignalRadar.Application.Feeds;
using SignalRadar.Application.Translation;
using SignalRadar.Bot.Discord;
using SignalRadar.Infrastructure.Articles;
using SignalRadar.Infrastructure.Database;
using SignalRadar.Infrastructure.Digests;
using SignalRadar.Infrastructure.Discord;
using SignalRadar.Infrastructure.ExternalSources;
using SignalRadar.Infrastructure.Feeds;
using SignalRadar.Infrastructure.Operations;
using SignalRadar.Infrastructure.Preferences;
using SignalRadar.Infrastructure.Publishing;
using SignalRadar.Infrastructure.Ranking;
using SignalRadar.Infrastructure.Translation;
using SignalRadar.Worker.Digests;
using SignalRadar.Worker.ExternalSources;
using SignalRadar.Worker.Feeds;
using SignalRadar.Worker.Operations;
using SignalRadar.Worker.Publishing;
using SignalRadar.Worker.Summaries;

DateTimeOffset startedAt = DateTimeOffset.UtcNow;
bool discordEnabled = ParseBoolean("DISCORD_ENABLED", defaultValue: true);
string connectionString = GetRequiredEnvironmentVariable(
    "DATABASE_CONNECTION_STRING");
StartupSecurityValidator.Validate(discordEnabled, connectionString);
RedactingConsoleLogger logger = new(
[
    connectionString,
    Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN"),
    Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
    Environment.GetEnvironmentVariable("GEMINI_API_KEY"),
    Environment.GetEnvironmentVariable("GITHUB_API_TOKEN")
]);
Action<string> log = logger.WriteLine;

if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    await using NpgsqlDataSource healthDataSource = NpgsqlDataSource.Create(
        connectionString);
    PostgresHealthCheck containerHealthCheck = new(healthDataSource);
    await containerHealthCheck.CheckAsync(CancellationToken.None);
    log("Health check succeeded.");
    return;
}

using CancellationTokenSource shutdown = new();
using CancellationTokenSource lifetime =
    CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

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

ulong[] allowedGuildIds = discordEnabled
    ? ParseSnowflakes("DISCORD_ALLOWED_GUILD_IDS", required: true)
    : [];
ulong[] allowedChannelIds = discordEnabled
    ? ParseSnowflakes("DISCORD_ALLOWED_CHANNEL_IDS", required: true)
    : [];
DiscordDigestScheduleOptions? digestSchedule =
    DiscordDigestScheduleConfiguration.Load(
        discordEnabled,
        allowedChannelIds);
DiscordAutomaticTopicPublishingOptions? topicPublishing =
    DiscordAutomaticTopicPublishingConfiguration.Load(
        discordEnabled,
        allowedChannelIds);

TimeProvider timeProvider = TimeProvider.System;
await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);

PostgresDatabaseMigrator migrator = new(dataSource);
await migrator.MigrateAsync(lifetime.Token);

PostgresHealthCheck healthCheck = new(dataSource);
await healthCheck.CheckAsync(lifetime.Token);
log("PostgreSQL migrations and startup health check completed.");

ArticleRankingProfileLoader rankingProfileLoader = new();
ArticleRankingProfile rankingProfile = await rankingProfileLoader.LoadAsync(
    Environment.GetEnvironmentVariable("RANKING_PROFILE_PATH"),
    lifetime.Token);
RuleBasedArticleAssessmentPolicy assessmentPolicy = new(rankingProfile);
log($"Loaded article ranking profile '{rankingProfile.Version}'.");

CanonicalUrlNormalizer urlNormalizer = new();
PostgresArticleInbox articleInbox = new(dataSource);
CollectArticleUseCase collectArticle = new(
    articleInbox,
    urlNormalizer,
    assessmentPolicy,
    timeProvider);
using SummaryRuntime summaryRuntime = SummaryRuntime.Create(
    dataSource,
    timeProvider);

if (summaryRuntime.Enabled)
{
    log($"Enabled article summaries with {summaryRuntime.Description}.");
}

List<Task> runningTasks = [];
DiscordInboxGateway? discordGateway = null;
HttpClient? feedHttpClient = null;
HttpClient? externalHttpClient = null;
HttpClient? translationHttpClient = null;
ITitleTranslator titleTranslator = new PassthroughTitleTranslator();

if (ParseBoolean("TITLE_TRANSLATION_ENABLED", defaultValue: true))
{
    string endpointValue = Environment.GetEnvironmentVariable(
        "TITLE_TRANSLATION_ENDPOINT") ?? "http://libretranslate:5000/";

    if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out Uri? translationEndpoint)
        || translationEndpoint.Scheme is not ("http" or "https"))
    {
        throw new InvalidOperationException(
            "TITLE_TRANSLATION_ENDPOINT must be an absolute HTTP(S) URL.");
    }

    SocketsHttpHandler translationHandler = CreateHttpHandler(
        ParseInteger(
            "TITLE_TRANSLATION_HTTP_MAX_CONNECTIONS_PER_SERVER",
            defaultValue: 2,
            minimum: 1,
            maximum: 8));
    translationHttpClient = new HttpClient(
        translationHandler,
        disposeHandler: true)
    {
        Timeout = TimeSpan.FromSeconds(ParseInteger(
            "TITLE_TRANSLATION_TIMEOUT_SECONDS",
            defaultValue: 10,
            minimum: 1,
            maximum: 60))
    };
    titleTranslator = new PostgresLibreTranslateTitleTranslator(
        dataSource,
        translationHttpClient,
        translationEndpoint,
        log);
    log($"Title translation enabled through {translationEndpoint.Host} with PostgreSQL cache.");
}

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
            log);

        runningTasks.Add(pollingLoop.RunAsync(lifetime.Token));
        log($"Loaded {definitions.Count} RSS/Atom source definitions.");
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
            log);

        runningTasks.Add(pollingLoop.RunAsync(lifetime.Token));
        log($"Loaded {definitions.Count} external API source definitions.");
    }

    if (discordEnabled)
    {
        DiscordInboxOptions discordOptions = new(
            GetRequiredEnvironmentVariable("DISCORD_BOT_TOKEN"),
            allowedGuildIds,
            allowedChannelIds,
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
        PostgresArticleRankingReader baseRankingReader = new(dataSource);
        PostgresSourcePreferenceStore sourcePreferenceStore = new(dataSource);
        IArticleRankingReader rankingReader = new TranslatingArticleRankingReader(
            new SourceFilteredArticleRankingReader(
                baseRankingReader,
                sourcePreferenceStore),
            titleTranslator);
        PostgresArticleFeedbackStore feedbackStore = new(dataSource);
        PostgresArticleSaveStore saveStore = new(dataSource);
        IArticleSavedReader savedReader = new TranslatingArticleSavedReader(
            saveStore,
            titleTranslator);
        SavedArticleMarkdownExporter markdownExporter = new();
        GenerateArticleDigestUseCase digestUseCase = new(
            rankingReader,
            timeProvider);
        PostgresSignalRadarStatusReader statusReader = new(
            dataSource,
            timeProvider,
            startedAt,
            rankingProfile.Version,
            summaryRuntime.Description,
            digestSchedule is not null);
        DiscordArticleInteractionService interactionService = new(
            rankingReader,
            feedbackStore,
            saveStore,
            savedReader,
            markdownExporter,
            timeProvider,
            summaryRuntime.UseCase);
        DiscordArticleInteractionHandler interactionHandler = new(
            discordOptions,
            interactionService,
            log,
            digestUseCase,
            statusReader);
        DiscordHelpCommandHandler helpCommandHandler = new(
            discordOptions,
            summaryRuntime.Enabled,
            digestEnabled: true,
            statusEnabled: true,
            automaticTopicPublishingEnabled: topicPublishing is not null,
            log);
        PostgresFeedSourceAdministrationStore feedAdministrationStore = new(dataSource);
        PostgresDiscordTopicRouteStore topicRouteStore = new(dataSource);
        DiscordManagementCommandHandler managementCommandHandler = new(
            discordOptions,
            feedAdministrationStore,
            topicRouteStore,
            sourcePreferenceStore,
            log);

        discordGateway = new DiscordInboxGateway(
            discordOptions,
            processor,
            mapper,
            log,
            interactionHandler,
            helpCommandHandler,
            managementCommandHandler);
        runningTasks.Add(discordGateway.RunAsync(lifetime.Token));

        if (topicPublishing is not null)
        {
            PostgresAutomaticTopicPublicationStore publicationStore = new(
                dataSource);
            DiscordAutomaticTopicPublisher publisher = new(
                topicPublishing,
                publicationStore,
                topicRouteStore,
                titleTranslator,
                discordGateway,
                timeProvider,
                log);
            runningTasks.Add(publisher.RunAsync(lifetime.Token));
            log(
                $"Batched automatic topic publishing enabled; "
                    + $"{topicPublishing.Routes.Count} legacy environment routes loaded, "
                    + "runtime routes are stored in PostgreSQL.");
        }

        if (digestSchedule is not null)
        {
            PostgresDigestDeliveryReceiptStore digestReceiptStore = new(dataSource);
            DiscordDigestScheduler scheduler = new(
                digestSchedule,
                digestUseCase,
                digestReceiptStore,
                discordGateway,
                timeProvider,
                log);
            runningTasks.Add(scheduler.RunAsync(lifetime.Token));
            log(
                $"Scheduled digest enabled for channel {digestSchedule.ChannelId} "
                    + $"in {digestSchedule.TimeZone.Id}.");
        }
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
    translationHttpClient?.Dispose();
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
