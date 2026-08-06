using Npgsql;
using SignalRadar.Application.Articles;
using SignalRadar.Bot.Discord;
using SignalRadar.Infrastructure.Articles;
using SignalRadar.Infrastructure.Database;
using SignalRadar.Infrastructure.Discord;

using CancellationTokenSource shutdown = new();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

DiscordInboxOptions options = new(
    GetRequiredEnvironmentVariable("DISCORD_BOT_TOKEN"),
    ParseSnowflakes("DISCORD_ALLOWED_GUILD_IDS", required: true),
    ParseSnowflakes("DISCORD_ALLOWED_CHANNEL_IDS", required: true),
    ParseSnowflakes("DISCORD_ALLOWED_AUTHOR_IDS", required: false),
    ParseBoolean("DISCORD_REQUIRE_AUTOMATED_AUTHOR", defaultValue: true),
    Environment.GetEnvironmentVariable("DISCORD_SOURCE_NAME") ?? "discord");

string connectionString = GetRequiredEnvironmentVariable(
    "DATABASE_CONNECTION_STRING");
await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);

PostgresDatabaseMigrator migrator = new(dataSource);
await migrator.MigrateAsync(shutdown.Token);

PostgresHealthCheck healthCheck = new(dataSource);
await healthCheck.CheckAsync(shutdown.Token);
Console.WriteLine("PostgreSQL migrations and startup health check completed.");

CanonicalUrlNormalizer urlNormalizer = new();
PostgresArticleInbox articleInbox = new(dataSource);
CollectArticleUseCase collectArticle = new(
    articleInbox,
    urlNormalizer,
    TimeProvider.System);

DiscordMessageAccessPolicy accessPolicy = new(options);
DiscordMessageArticleCandidateFactory candidateFactory = new();
PostgresDiscordMessageReceiptStore receiptStore = new(
    dataSource,
    TimeSpan.FromMinutes(5));
DiscordInboxProcessor processor = new(
    options,
    accessPolicy,
    candidateFactory,
    receiptStore,
    collectArticle);
DiscordSocketMessageMapper mapper = new();

using DiscordInboxGateway gateway = new(
    options,
    processor,
    mapper,
    Console.WriteLine);

await gateway.RunAsync(shutdown.Token);

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
