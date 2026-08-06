using System.Buffers;
using System.Net;
using System.Net.Http.Headers;

namespace SignalRadar.Infrastructure.ExternalSources;

public sealed record JsonHttpResult(
    bool NotModified,
    byte[] Content,
    string? ETag,
    DateTimeOffset? LastModified);

public sealed class BoundedJsonHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeout;
    private readonly int _maximumResponseBytes;
    private readonly int _maximumAttempts;

    public BoundedJsonHttpClient(
        HttpClient httpClient,
        TimeProvider timeProvider,
        TimeSpan timeout,
        int maximumResponseBytes,
        int maximumAttempts)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

        if (timeout < TimeSpan.FromSeconds(1)
            || timeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        if (maximumResponseBytes is < 1024 or > 16 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes));
        }

        if (maximumAttempts is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        }

        _timeout = timeout;
        _maximumResponseBytes = maximumResponseBytes;
        _maximumAttempts = maximumAttempts;
    }

    public async ValueTask<JsonHttpResult> GetAsync(
        Uri endpoint,
        string? etag,
        DateTimeOffset? lastModified,
        Action<HttpRequestMessage>? configure,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        for (int attempt = 1; attempt <= _maximumAttempts; attempt++)
        {
            try
            {
                using HttpRequestMessage request = new(HttpMethod.Get, endpoint);
                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));

                if (!string.IsNullOrWhiteSpace(etag))
                {
                    request.Headers.TryAddWithoutValidation("If-None-Match", etag);
                }

                if (lastModified.HasValue)
                {
                    request.Headers.IfModifiedSince = lastModified;
                }

                configure?.Invoke(request);

                using CancellationTokenSource timeout =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_timeout);
                using HttpResponseMessage response = await _httpClient
                    .SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token)
                    .ConfigureAwait(false);
                string? responseEtag = response.Headers.ETag?.ToString() ?? etag;
                DateTimeOffset? responseLastModified =
                    response.Content.Headers.LastModified ?? lastModified;

                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    return new JsonHttpResult(
                        true,
                        [],
                        responseEtag,
                        responseLastModified);
                }

                if (!response.IsSuccessStatusCode)
                {
                    if (attempt < _maximumAttempts
                        && IsTransient(response.StatusCode))
                    {
                        await DelayBeforeRetryAsync(
                            response.Headers.RetryAfter,
                            attempt,
                            cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    throw new HttpRequestException(
                        $"JSON endpoint returned HTTP {(int)response.StatusCode}.",
                        null,
                        response.StatusCode);
                }

                byte[] content = await ReadBoundedAsync(
                    response.Content,
                    timeout.Token).ConfigureAwait(false);
                return new JsonHttpResult(
                    false,
                    content,
                    responseEtag,
                    responseLastModified);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                attempt < _maximumAttempts && IsTransient(exception))
            {
                await DelayBeforeRetryAsync(
                    null,
                    attempt,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("The JSON request exhausted all attempts.");
    }

    private async ValueTask<byte[]> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > _maximumResponseBytes)
        {
            throw new InvalidDataException(
                "The JSON response exceeds the configured size limit.");
        }

        await using Stream stream = await content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using MemoryStream output = new();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);

        try
        {
            int total = 0;

            while (true)
            {
                int read = await stream
                    .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);

                if (total > _maximumResponseBytes)
                {
                    throw new InvalidDataException(
                        "The JSON response exceeds the configured size limit.");
                }

                output.Write(buffer, 0, read);
            }

            return output.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async ValueTask DelayBeforeRetryAsync(
        RetryConditionHeaderValue? retryAfter,
        int attempt,
        CancellationToken cancellationToken)
    {
        TimeSpan delay = retryAfter?.Delta
            ?? TimeSpan.FromMilliseconds(250 * attempt);

        if (retryAfter?.Date is DateTimeOffset retryDate)
        {
            TimeSpan serverDelay = retryDate - _timeProvider.GetUtcNow();
            delay = serverDelay > TimeSpan.Zero ? serverDelay : TimeSpan.Zero;
        }

        if (delay > TimeSpan.FromSeconds(30))
        {
            delay = TimeSpan.FromSeconds(30);
        }

        await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        int code = (int)statusCode;
        return statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            || code >= 500;
    }

    private static bool IsTransient(Exception exception)
    {
        return exception is HttpRequestException
            or IOException
            or OperationCanceledException;
    }
}
