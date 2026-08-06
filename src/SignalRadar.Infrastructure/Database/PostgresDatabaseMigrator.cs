using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace SignalRadar.Infrastructure.Database;

public sealed class PostgresDatabaseMigrator
{
    private const string MigrationResourcePrefix =
        "SignalRadar.Infrastructure.Database.Migrations.";

    private const string CreateMigrationTableSql = """
        CREATE TABLE IF NOT EXISTS schema_migrations (
            version integer PRIMARY KEY,
            name text NOT NULL,
            checksum character(64) NOT NULL,
            applied_at timestamptz NOT NULL DEFAULT clock_timestamp()
        );
        """;

    private const string AcquireMigrationLockSql =
        "SELECT pg_advisory_xact_lock(741356117304801);";

    private readonly NpgsqlDataSource _dataSource;

    public PostgresDatabaseMigrator(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async ValueTask MigrateAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await _dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using NpgsqlTransaction transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            CreateMigrationTableSql,
            cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(
            connection,
            transaction,
            AcquireMigrationLockSql,
            cancellationToken).ConfigureAwait(false);

        Assembly assembly = typeof(PostgresDatabaseMigrator).Assembly;
        string[] resources = assembly
            .GetManifestResourceNames()
            .Where(static name => name.StartsWith(
                MigrationResourcePrefix,
                StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (resources.Length == 0)
        {
            throw new InvalidOperationException(
                "No embedded PostgreSQL migrations were found.");
        }

        foreach (string resourceName in resources)
        {
            await ApplyMigrationAsync(
                assembly,
                resourceName,
                connection,
                transaction,
                cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ApplyMigrationAsync(
        Assembly assembly,
        string resourceName,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        string fileName = resourceName[MigrationResourcePrefix.Length..];
        int separatorIndex = fileName.IndexOf('_', StringComparison.Ordinal);

        if (separatorIndex <= 0
            || !int.TryParse(
                fileName.AsSpan(0, separatorIndex),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int version))
        {
            throw new InvalidOperationException(
                $"Migration resource '{resourceName}' has an invalid name.");
        }

        await using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Migration resource '{resourceName}' could not be opened.");
        using StreamReader reader = new(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        string sql = await reader
            .ReadToEndAsync(cancellationToken)
            .ConfigureAwait(false);
        string checksum = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(sql)));

        string? appliedChecksum = await GetAppliedChecksumAsync(
            connection,
            transaction,
            version,
            cancellationToken).ConfigureAwait(false);

        if (appliedChecksum is not null)
        {
            if (!string.Equals(appliedChecksum, checksum, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Applied migration {version} has a checksum mismatch.");
            }

            return;
        }

        await ExecuteNonQueryAsync(
            connection,
            transaction,
            sql,
            cancellationToken).ConfigureAwait(false);

        const string insertSql = """
            INSERT INTO schema_migrations (version, name, checksum)
            VALUES ($1, $2, $3);
            """;

        await using NpgsqlCommand insertCommand = new(
            insertSql,
            connection,
            transaction);
        insertCommand.Parameters.AddWithValue(version);
        insertCommand.Parameters.AddWithValue(fileName);
        insertCommand.Parameters.AddWithValue(checksum);
        await insertCommand
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask<string?> GetAppliedChecksumAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int version,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT checksum
            FROM schema_migrations
            WHERE version = $1;
            """;

        await using NpgsqlCommand command = new(sql, connection, transaction);
        command.Parameters.AddWithValue(version);
        object? result = await command
            .ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);

        return result as string;
    }

    private static async ValueTask ExecuteNonQueryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = new(sql, connection, transaction);
        await command
            .ExecuteNonQueryAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
