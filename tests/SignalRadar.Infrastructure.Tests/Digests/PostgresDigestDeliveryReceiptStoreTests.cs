using Npgsql;
using SignalRadar.Application.Digests;
using SignalRadar.Infrastructure.Database;
using SignalRadar.Infrastructure.Digests;
using Xunit;

namespace SignalRadar.Infrastructure.Tests.Digests;

public sealed class PostgresDigestDeliveryReceiptStoreTests
{
    [Fact]
    public async Task ReceiptLeaseRetriesFailureAndPreventsCompletedRedelivery()
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
        PostgresDigestDeliveryReceiptStore store = new(dataSource);
        string key = $"test:{Guid.NewGuid():N}";
        DateTimeOffset window = new(2026, 8, 6, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = window.AddHours(8);

        DigestDeliveryLease? first = await store.TryBeginAsync(
            key,
            window,
            now,
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        Assert.NotNull(first);
        Assert.Null(await store.TryBeginAsync(
            key,
            window,
            now.AddMinutes(1),
            TimeSpan.FromMinutes(5),
            CancellationToken.None));

        await store.FailAsync(
            first,
            now.AddMinutes(1),
            "temporary failure",
            CancellationToken.None);
        DigestDeliveryLease? retry = await store.TryBeginAsync(
            key,
            window,
            now.AddMinutes(2),
            TimeSpan.FromMinutes(5),
            CancellationToken.None);
        Assert.NotNull(retry);
        await store.CompleteAsync(
            retry,
            now.AddMinutes(3),
            ulong.MaxValue,
            CancellationToken.None);

        Assert.Null(await store.TryBeginAsync(
            key,
            window,
            now.AddDays(1),
            TimeSpan.FromMinutes(5),
            CancellationToken.None));
    }
}
