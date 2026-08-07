using Npgsql;
using SignalRadar.Application.Articles;
using SignalRadar.Infrastructure.Articles;
using SignalRadar.Infrastructure.Database;
using SignalRadar.Infrastructure.Operations;
using SignalRadar.Infrastructure.Ranking;
using SignalRadar.Worker.Runtime;
using SignalRadar.Worker.Summaries;

DateTimeOffset startedAt = DateTimeOffset.UtcNow;
bool discordEnabled = WorkerEnvironment.ParseBoolean(
    "DISCORD_ENABLED",
    defaultValue: true);
string connectionString = WorkerEnvironment.GetRequired(
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
bool hackerNewsEnabled = WorkerEnvironment.ParseBoolean(
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

List<IDisposable> runtimes = [];
List<Task> runningTasks = [];

try
{
    if (feedsEnabled)
    {
        FeedRuntime feedRuntime = await FeedRuntime.StartAsync(
            dataSource,
            collectArticle,
            timeProvider,
            feedConfigurationPath!,
            log,
            lifetime.Token).ConfigureAwait(false);
        runtimes.Add(feedRuntime);
        runningTasks.Add(feedRuntime.RunTask);
    }

    if (externalSourcesEnabled)
    {
        ExternalSourceRuntime externalRuntime =
            await ExternalSourceRuntime.StartAsync(
                dataSource,
                collectArticle,
                timeProvider,
                githubConfigurationPath,
                hackerNewsEnabled,
                log,
                lifetime.Token).ConfigureAwait(false);
        runtimes.Add(externalRuntime);
        runningTasks.Add(externalRuntime.RunTask);
    }

    if (discordEnabled)
    {
        DiscordRuntime discordRuntime = DiscordRuntime.Start(
            dataSource,
            collectArticle,
            assessmentPolicy,
            rankingProfile.Version,
            timeProvider,
            startedAt,
            summaryRuntime,
            log,
            lifetime.Token);
        runtimes.Add(discordRuntime);
        runningTasks.AddRange(discordRuntime.Tasks);
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

    for (int index = runtimes.Count - 1; index >= 0; index--)
    {
        runtimes[index].Dispose();
    }
}
