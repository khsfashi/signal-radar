using Npgsql;

namespace SignalRadar.Infrastructure.Database;

public sealed class PostgresHealthCheck
{
    private readonly NpgsqlDataSource _dataSource;

    public PostgresHealthCheck(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async ValueTask CheckAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = _dataSource.CreateCommand("SELECT 1;");
        object? result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        if (result is not int value || value != 1)
        {
            throw new InvalidOperationException(
                "PostgreSQL health check returned an unexpected result.");
        }
    }
}
