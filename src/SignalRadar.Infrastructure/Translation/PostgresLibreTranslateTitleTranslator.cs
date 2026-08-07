using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using SignalRadar.Application.Translation;

namespace SignalRadar.Infrastructure.Translation;

public sealed class PostgresLibreTranslateTitleTranslator : ITitleTranslator
{
    private const string TargetLanguage = "ko";
    private const string ProviderIdentity = "libretranslate-v1.9.6";
    private readonly NpgsqlDataSource _dataSource;
    private readonly HttpClient _httpClient;
    private readonly Uri _translateEndpoint;
    private readonly Action<string> _log;

    public PostgresLibreTranslateTitleTranslator(
        NpgsqlDataSource dataSource,
        HttpClient httpClient,
        Uri baseEndpoint,
        Action<string>? log = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ArgumentNullException.ThrowIfNull(baseEndpoint);
        _translateEndpoint = new Uri(
            baseEndpoint.AbsoluteUri.EndsWith('/', StringComparison.Ordinal)
                ? baseEndpoint
                : new Uri(baseEndpoint.AbsoluteUri + "/"),
            "translate");
        _log = log ?? (static _ => { });
    }

    public async ValueTask<string> TranslateAsync(
        string title,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        string normalized = title.Trim();

        if (ContainsHangul(normalized))
        {
            return normalized;
        }

        string hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        string? cached = await TryReadCacheAsync(hash, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        try
        {
            using HttpRequestMessage request = new(
                HttpMethod.Post,
                _translateEndpoint)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new TranslationRequest(
                        normalized,
                        "auto",
                        TargetLanguage,
                        "text")),
                    Encoding.UTF8,
                    "application/json")
            };
            using HttpResponseMessage response = await _httpClient
                .SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            TranslationResponse? payload = await JsonSerializer
                .DeserializeAsync<TranslationResponse>(
                    stream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            string? translated = payload?.TranslatedText?.Trim();

            if (string.IsNullOrWhiteSpace(translated))
            {
                return normalized;
            }

            if (translated.Length > 1000)
            {
                translated = translated[..1000];
            }

            await WriteCacheAsync(hash, translated, cancellationToken)
                .ConfigureAwait(false);
            return translated;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _log(
                $"Title translation failed: {exception.GetType().Name}: "
                    + $"{exception.Message}");
            return normalized;
        }
    }

    private async ValueTask<string?> TryReadCacheAsync(
        string hash,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT translated_title
            FROM title_translation_cache
            WHERE source_hash = $1
                AND target_language = $2
                AND provider = $3;
            """;
        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(hash);
        command.Parameters.AddWithValue(TargetLanguage);
        command.Parameters.AddWithValue(ProviderIdentity);
        object? value = await command.ExecuteScalarAsync(cancellationToken)
            .ConfigureAwait(false);
        return value as string;
    }

    private async ValueTask WriteCacheAsync(
        string hash,
        string translated,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO title_translation_cache (
                source_hash,
                target_language,
                provider,
                translated_title)
            VALUES ($1, $2, $3, $4)
            ON CONFLICT (source_hash, target_language, provider) DO UPDATE
            SET translated_title = EXCLUDED.translated_title,
                updated_at = clock_timestamp();
            """;
        await using NpgsqlCommand command = _dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(hash);
        command.Parameters.AddWithValue(TargetLanguage);
        command.Parameters.AddWithValue(ProviderIdentity);
        command.Parameters.AddWithValue(translated);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool ContainsHangul(string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];

            if (character is >= '\uAC00' and <= '\uD7A3'
                || character is >= '\u1100' and <= '\u11FF'
                || character is >= '\u3130' and <= '\u318F')
            {
                return true;
            }
        }

        return false;
    }

    private sealed record TranslationRequest(
        [property: JsonPropertyName("q")] string Q,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("target")] string Target,
        [property: JsonPropertyName("format")] string Format);

    private sealed class TranslationResponse
    {
        [JsonPropertyName("translatedText")]
        public string? TranslatedText { get; init; }
    }
}
