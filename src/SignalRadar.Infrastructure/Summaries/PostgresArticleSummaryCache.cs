using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SignalRadar.Application.Summaries;

namespace SignalRadar.Infrastructure.Summaries;

public sealed class PostgresArticleSummaryCache : IArticleSummaryCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly NpgsqlDataSource _dataSource;

    public PostgresArticleSummaryCache(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async ValueTask<ArticleSummaryCacheEntry?> TryGetAsync(
        string inputHash,
        CancellationToken cancellationToken)
    {
        ValidateHash(inputHash);
        const string sql = """
            SELECT provider,
                   model,
                   prompt_version,
                   language,
                   article_count,
                   summary_json::text,
                   generated_at,
                   provider_response_id
            FROM article_summary_cache
            WHERE input_hash = $1;
            """;

        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(inputHash);
        await using NpgsqlDataReader reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        string summaryJson = reader.GetString(5);
        ArticleSummaryContent content = JsonSerializer.Deserialize<ArticleSummaryContent>(
            summaryJson,
            JsonOptions)
            ?? throw new InvalidOperationException(
                "The cached article summary JSON was empty.");

        return new ArticleSummaryCacheEntry(
            inputHash,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt16(4),
            ArticleSummaryContentValidator.Normalize(content),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetString(7));
    }

    public async ValueTask StoreAsync(
        ArticleSummaryCacheEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ValidateHash(entry.InputHash);
        string summaryJson = JsonSerializer.Serialize(entry.Content, JsonOptions);
        const string sql = """
            INSERT INTO article_summary_cache (
                input_hash,
                provider,
                model,
                prompt_version,
                language,
                article_count,
                summary_json,
                provider_response_id,
                generated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            ON CONFLICT (input_hash) DO NOTHING;
            """;

        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(entry.InputHash);
        command.Parameters.AddWithValue(entry.Provider);
        command.Parameters.AddWithValue(entry.Model);
        command.Parameters.AddWithValue(entry.PromptVersion);
        command.Parameters.AddWithValue(entry.Language);
        command.Parameters.AddWithValue((short)entry.ArticleCount);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Jsonb,
            Value = summaryJson
        });
        command.Parameters.AddWithValue(
            entry.ProviderResponseId is null
                ? DBNull.Value
                : entry.ProviderResponseId);
        command.Parameters.AddWithValue(entry.GeneratedAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateHash(string inputHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputHash);

        if (inputHash.Length != 64)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inputHash),
                "The summary input hash must contain 64 hexadecimal characters.");
        }

        for (int index = 0; index < inputHash.Length; index++)
        {
            char value = inputHash[index];

            if (!char.IsAsciiHexDigit(value) || char.IsLower(value))
            {
                throw new ArgumentException(
                    "The summary input hash must be uppercase hexadecimal.",
                    nameof(inputHash));
            }
        }
    }
}
