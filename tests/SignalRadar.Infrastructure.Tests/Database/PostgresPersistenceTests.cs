using Npgsql;
using SignalRadar.Application.Discord;
using SignalRadar.Domain.Articles;
using SignalRadar.Infrastructure.Articles;
using SignalRadar.Infrastructure.Database;
using SignalRadar.Infrastructure.Discord;
using Xunit;

namespace SignalRadar.Infrastructure.Tests.Database;

public sealed class PostgresPersistenceTests
{
    [Fact]
    public async Task StoresPersistDuplicateGuardsAcrossInstances()
    {
        string? connectionString = Environment.GetEnvironmentVariable(
            "SIGNAL_RADAR_TEST_DATABASE_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(
            connectionString);
        PostgresDatabaseMigrator migrator = new(dataSource);
        await migrator.MigrateAsync(CancellationToken.None);
        await ResetTablesAsync(dataSource);

        Article article = Article.Create(
            "PostgreSQL persistence",
            new Uri("https://example.com/postgres"),
            "integration-test",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        PostgresArticleInbox firstArticleStore = new(dataSource);
        PostgresArticleInbox secondArticleStore = new(dataSource);

        Assert.True(await firstArticleStore.TryAddAsync(
            article,
            CancellationToken.None));
        Assert.False(await secondArticleStore.TryAddAsync(
            article,
            CancellationToken.None));

        PostgresDiscordMessageReceiptStore firstReceiptStore = new(
            dataSource,
            TimeSpan.FromMinutes(1));
        PostgresDiscordMessageReceiptStore secondReceiptStore = new(
            dataSource,
            TimeSpan.FromMinutes(1));

        DiscordMessageReceiptLease? lease = await firstReceiptStore.TryBeginAsync(
            ulong.MaxValue,
            CancellationToken.None);

        Assert.NotNull(lease);
        await firstReceiptStore.CompleteAsync(
            lease,
            CancellationToken.None);
        Assert.Null(await secondReceiptStore.TryBeginAsync(
            ulong.MaxValue,
            CancellationToken.None));
    }

    private static async Task ResetTablesAsync(NpgsqlDataSource dataSource)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "TRUNCATE TABLE discord_message_receipts, articles;");
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
