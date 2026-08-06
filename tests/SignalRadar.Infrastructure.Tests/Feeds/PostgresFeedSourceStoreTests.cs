using Npgsql;
using SignalRadar.Application.Feeds;
using SignalRadar.Infrastructure.Database;
using SignalRadar.Infrastructure.Feeds;
using Xunit;

namespace SignalRadar.Infrastructure.Tests.Feeds;

public sealed class PostgresFeedSourceStoreTests
{
    [Fact]
    public async Task SourceLeaseAndFailureStatePersistAcrossStoreInstances()
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
        await migrator.MigrateAsync(TestContext.Current.CancellationToken);
        await ResetTableAsync(dataSource);

        PostgresFeedSourceStore firstStore = new(dataSource);
        PostgresFeedSourceStore secondStore = new(dataSource);
        await firstStore.UpsertDefinitionsAsync(
            [
                new FeedSourceDefinition(
                    "official-test-feed",
                    new Uri("https://example.com/feed.xml"),
                    TimeSpan.FromMinutes(15))
            ],
            TestContext.Current.CancellationToken);

        FeedSourceLease lease = Assert.Single(await firstStore.ClaimDueAsync(
            1,
            TimeSpan.FromMinutes(2),
            TestContext.Current.CancellationToken));
        Assert.Empty(await secondStore.ClaimDueAsync(
            1,
            TimeSpan.FromMinutes(2),
            TestContext.Current.CancellationToken));

        DateTimeOffset failedAt = DateTimeOffset.UtcNow;
        await firstStore.CompleteFailureAsync(
            lease,
            "test failure",
            failedAt,
            failedAt.AddHours(1),
            null,
            TestContext.Current.CancellationToken);

        Assert.Empty(await secondStore.ClaimDueAsync(
            1,
            TimeSpan.FromMinutes(2),
            TestContext.Current.CancellationToken));
        Assert.Equal(1, await ReadFailureCountAsync(dataSource));
    }

    private static async Task ResetTableAsync(NpgsqlDataSource dataSource)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "TRUNCATE TABLE feed_sources;");
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<int> ReadFailureCountAsync(
        NpgsqlDataSource dataSource)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "SELECT consecutive_failures FROM feed_sources LIMIT 1;");
        object? result = await command.ExecuteScalarAsync(
            TestContext.Current.CancellationToken);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
