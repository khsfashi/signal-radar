using Npgsql;
using SignalRadar.Application.Operations;

namespace SignalRadar.Infrastructure.Operations;

public sealed class PostgresSignalRadarStatusReader : ISignalRadarStatusReader
{
    private const string Sql = """
        SELECT (SELECT count(*) FROM articles),
               (SELECT max(collected_at) FROM articles),
               (SELECT count(*) FROM feed_sources WHERE enabled),
               (SELECT count(*) FROM external_sources WHERE enabled),
               (SELECT count(*) FROM article_saves),
               (SELECT count(*) FROM article_summary_cache),
               (SELECT count(*) FROM article_content_cache);
        """;

    private readonly NpgsqlDataSource _dataSource;
    private readonly TimeProvider _timeProvider;
    private readonly DateTimeOffset _startedAt;
    private readonly string _rankingProfileVersion;
    private readonly string _summaryProvider;
    private readonly bool _digestSchedulerEnabled;

    public PostgresSignalRadarStatusReader(
        NpgsqlDataSource dataSource,
        TimeProvider timeProvider,
        DateTimeOffset startedAt,
        string rankingProfileVersion,
        string? summaryProvider,
        bool digestSchedulerEnabled)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _startedAt = startedAt.ToUniversalTime();
        ArgumentException.ThrowIfNullOrWhiteSpace(rankingProfileVersion);
        _rankingProfileVersion = rankingProfileVersion.Trim();
        _summaryProvider = string.IsNullOrWhiteSpace(summaryProvider)
            ? "disabled"
            : summaryProvider.Trim();
        _digestSchedulerEnabled = digestSchedulerEnabled;
    }

    public async ValueTask<SignalRadarStatusSnapshot> ReadAsync(
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = _dataSource.CreateCommand(Sql);
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        bool hasRow = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        if (!hasRow)
        {
            throw new InvalidOperationException(
                "The operational status query returned no row.");
        }

        return new SignalRadarStatusSnapshot(
            _timeProvider.GetUtcNow(),
            _startedAt,
            reader.GetInt64(0),
            reader.IsDBNull(1)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(1),
            checked((int)reader.GetInt64(2)),
            checked((int)reader.GetInt64(3)),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6),
            _rankingProfileVersion,
            _summaryProvider,
            _digestSchedulerEnabled);
    }
}
