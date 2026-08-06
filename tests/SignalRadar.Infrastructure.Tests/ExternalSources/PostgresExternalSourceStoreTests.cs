using Npgsql;
using SignalRadar.Application.ExternalSources;
using SignalRadar.Infrastructure.Database;
using SignalRadar.Infrastructure.ExternalSources;
using Xunit;

namespace SignalRadar.Infrastructure.Tests.ExternalSources;

public sealed class PostgresExternalSourceStoreTests
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
        await migrator.MigrateAsync(CancellationToken.None);
        await ResetTableAsync(dataSource);

        PostgresExternalSourceStore firstStore = new(dataSource);
        PostgresExternalSourceStore secondStore = new(dataSource);
        await firstStore.UpsertDefinitionsAsync(
            [
                new ExternalSourceDefinition(
                    ExternalSourceTypes.GitHubReleases,
                    "github:example/project",
                    "Example Project",
                    new Uri(
                        "https://api.github.com/repos/example/project/releases"),
                    TimeSpan.FromMinutes(30),
                    "{}")
            ],
            CancellationToken.None);

        ExternalSourceLease lease = Assert.Single(await firstStore.ClaimDueAsync(
            1,
            TimeSpan.FromMinutes(2),
            CancellationToken.None));
        Assert.Empty(await secondStore.ClaimDueAsync(
            1,
            TimeSpan.FromMinutes(2),
            CancellationToken.None));

        DateTimeOffset failedAt = DateTimeOffset.UtcNow;
        await firstStore.CompleteFailureAsync(
            lease,
            "test failure",
            failedAt,
            failedAt.AddHours(1),
            null,
            CancellationToken.None);

        Assert.Empty(await secondStore.ClaimDueAsync(
            1,
            TimeSpan.FromMinutes(2),
            CancellationToken.None));
        Assert.Equal(1, await ReadFailureCountAsync(dataSource));
    }

    private static async Task ResetTableAsync(NpgsqlDataSource dataSource)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "TRUNCATE TABLE external_sources;");
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<int> ReadFailureCountAsync(
        NpgsqlDataSource dataSource)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "SELECT consecutive_failures FROM external_sources LIMIT 1;");
        object? result = await command.ExecuteScalarAsync(CancellationToken.None);
        return Convert.ToInt32(
            result,
            System.Globalization.CultureInfo.InvariantCulture);
    }
}
