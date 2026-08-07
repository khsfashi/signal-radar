using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using SignalRadar.Application.Feeds;

namespace SignalRadar.Infrastructure.Feeds;

public sealed class FeedHttpOptions
{
    public FeedHttpOptions(
        TimeSpan requestTimeout,
        int maximumResponseBytes,
        int maximumAttempts = 3,
        int maximumRedirects = 5,
        TimeSpan? baseRetryDelay = null,
        bool allowPrivateNetworkTargets = false)
    {
        if (requestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        if (maximumResponseBytes is < 1024 or > 16 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes));
        }

        if (maximumAttempts is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        }

        if (maximumRedirects is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRedirects));
        }

        TimeSpan retryDelay = baseRetryDelay ?? TimeSpan.FromSeconds(1);

        if (retryDelay < TimeSpan.Zero || retryDelay > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(baseRetryDelay));
        }

        RequestTimeout = requestTimeout;
        MaximumResponseBytes = maximumResponseBytes;
        MaximumAttempts = maximumAttempts;
        MaximumRedirects = maximumRedirects;
        BaseRetryDelay = retryDelay;
        AllowPrivateNetworkTargets = allowPrivateNetworkTargets;
    }

    public TimeSpan RequestTimeout { get; }

    public int MaximumResponseBytes { get; }

    public int MaximumAttempts { get; }

    public int MaximumRedirects { get; }

    public TimeSpan BaseRetryDelay { get; }

    public bool AllowPrivateNetworkTargets { get; }
}

public sealed class HttpFeedDocumentFetcher : IFeedDocumentFetcher
{
    private readonly HttpClient _httpClient;
    private readonly FeedHttpOptions _options;
    private readonly TimeProvider _timeProvider;

    public HttpFeedDocumentFetcher(
        HttpClient httpClient,
        FeedHttpOptions options,
        TimeProvider timeProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async ValueTask<FeedFetchResult> FetchAsync(
        FeedSourceLease source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        Exception? lastException = null;

        for (int attempt = 1; attempt <= _options.MaximumAttempts; attempt++)
        {
            try
            {
                return await SendWithRedirectsAsync(source, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TransientFeedHttpException exception)
                when (attempt < _options.MaximumAttempts)
            {
                lastException = exception;
                await DelayBeforeRetryAsync(
                    attempt,
                    exception.RetryAfter,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException exception)
                when (attempt < _options.MaximumAttempts)
            {
                lastException = exception;
                await DelayBeforeRetryAsync(
                    attempt,
                    null,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
                when (attempt < _options.MaximumAttempts
                    && IsTransientStatus(exception.StatusCode))
            {
                lastException = exception;
                await DelayBeforeRetryAsync(
                    attempt,
                    null,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        throw lastException
            ?? new InvalidOperationException("Feed download failed without an exception.");
    }

    private async ValueTask<FeedFetchResult> SendWithRedirectsAsync(
        FeedSourceLease source,
        CancellationToken cancellationToken)
    {
        Uri requestUri = source.FeedUrl;

        for (int redirectCount = 0; ; redirectCount++)
        {
            await ValidateTargetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            using HttpRequestMessage request = CreateRequest(source, requestUri);
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.RequestTimeout);

            HttpResponseMessage response;

            try
            {
                response = await _httpClient
                    .SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
                when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Feed request exceeded {_options.RequestTimeout}.",
                    exception);
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotModified)
                {
                    return FeedFetchResult.NotModified(
                        response.Headers.ETag?.ToString() ?? source.ETag,
                        response.Content?.Headers.LastModified ?? source.LastModified);
                }

                if (IsRedirect(response.StatusCode))
                {
                    if (redirectCount >= _options.MaximumRedirects)
                    {
                        throw new HttpRequestException(
                            "The feed exceeded the redirect limit.",
                            null,
                            response.StatusCode);
                    }

                    Uri? location = response.Headers.Location;

                    if (location is null)
                    {
                        throw new HttpRequestException(
                            "The feed returned a redirect without a Location header.",
                            null,
                            response.StatusCode);
                    }

                    requestUri = location.IsAbsoluteUri
                        ? location
                        : new Uri(requestUri, location);
                    continue;
                }

                if (IsTransientStatus(response.StatusCode))
                {
                    throw new TransientFeedHttpException(
                        response.StatusCode,
                        GetRetryDelay(response.Headers.RetryAfter));
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"Feed request returned HTTP {(int)response.StatusCode}.",
                        null,
                        response.StatusCode);
                }

                HttpContent responseContent = response.Content
                    ?? throw new InvalidDataException(
                        "The feed response did not contain a body.");
                byte[] content = await ReadBoundedContentAsync(
                    responseContent,
                    timeout.Token).ConfigureAwait(false);

                return FeedFetchResult.Downloaded(
                    content,
                    response.Headers.ETag?.ToString(),
                    responseContent.Headers.LastModified);
            }
        }
    }

    private HttpRequestMessage CreateRequest(
        FeedSourceLease source,
        Uri requestUri)
    {
        HttpRequestMessage request = new(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.ParseAdd("SignalRadar/0.1 (+https://github.com/khsfashi/signal-radar)");

        request.Headers.Accept.ParseAdd(
            "application/atom+xml, application/rss+xml, application/xml;q=0.9, text/xml;q=0.8");

        if (!string.IsNullOrWhiteSpace(source.ETag)
            && EntityTagHeaderValue.TryParse(
                source.ETag,
                out EntityTagHeaderValue? entityTag)
            && entityTag is not null)
        {
            request.Headers.IfNoneMatch.Add(entityTag);
        }

        if (source.LastModified.HasValue)
        {
            request.Headers.IfModifiedSince = source.LastModified;
        }

        return request;
    }

    private async ValueTask<byte[]> ReadBoundedContentAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        long? contentLength = content.Headers.ContentLength;

        if (contentLength > _options.MaximumResponseBytes)
        {
            throw new InvalidDataException(
                "The feed response exceeded the configured size limit.");
        }

        int initialCapacity = contentLength.HasValue
            ? checked((int)contentLength.Value)
            : Math.Min(_options.MaximumResponseBytes, 64 * 1024);
        using MemoryStream output = new(initialCapacity);
        await using Stream input = await content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);

        try
        {
            int totalRead = 0;

            while (true)
            {
                int read = await input
                    .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                totalRead += read;

                if (totalRead > _options.MaximumResponseBytes)
                {
                    throw new InvalidDataException(
                        "The feed response exceeded the configured size limit.");
                }

                await output
                    .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return output.ToArray();
    }

    private async ValueTask ValidateTargetAsync(
        Uri target,
        CancellationToken cancellationToken)
    {
        if (!target.IsAbsoluteUri || target.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException(
                "Feed targets must use absolute HTTP or HTTPS URLs.");
        }

        if (_options.AllowPrivateNetworkTargets)
        {
            return;
        }

        string host = target.DnsSafeHost;

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Private-network feed targets are disabled.");
        }

        IPAddress[] addresses;

        if (IPAddress.TryParse(host, out IPAddress? literalAddress))
        {
            addresses = [literalAddress];
        }
        else
        {
            try
            {
                addresses = await Dns
                    .GetHostAddressesAsync(host, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (SocketException exception)
            {
                throw new HttpRequestException(
                    $"The feed host '{host}' could not be resolved.",
                    exception);
            }
        }

        if (addresses.Length == 0)
        {
            throw new HttpRequestException(
                $"The feed host '{host}' resolved to no addresses.");
        }

        for (int index = 0; index < addresses.Length; index++)
        {
            if (!IsPublicAddress(addresses[index]))
            {
                throw new InvalidOperationException(
                    "Private-network feed targets are disabled.");
            }
        }
    }

    private async ValueTask DelayBeforeRetryAsync(
        int attempt,
        TimeSpan? retryAfter,
        CancellationToken cancellationToken)
    {
        double multiplier = Math.Pow(2, attempt - 1);
        TimeSpan calculated = TimeSpan.FromMilliseconds(
            Math.Min(
                _options.BaseRetryDelay.TotalMilliseconds * multiplier,
                TimeSpan.FromSeconds(30).TotalMilliseconds));
        TimeSpan requestedDelay = retryAfter.HasValue && retryAfter.Value > calculated
            ? retryAfter.Value
            : calculated;
        TimeSpan delay = requestedDelay <= TimeSpan.FromMinutes(5)
            ? requestedDelay
            : TimeSpan.FromMinutes(5);

        if (delay > TimeSpan.Zero)
        {
            await Task
                .Delay(delay, _timeProvider, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
    }

    private static bool IsTransientStatus(HttpStatusCode? statusCode)
    {
        return !statusCode.HasValue
            || statusCode is HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
            || (int)statusCode.Value >= 500;
    }

    private TimeSpan? GetRetryDelay(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta.HasValue)
        {
            return retryAfter.Delta.Value;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        return retryAfter.Date.HasValue && retryAfter.Date.Value > now
            ? retryAfter.Date.Value - now
            : null;
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = address.GetAddressBytes();
            return bytes[0] != 0
                && bytes[0] != 10
                && bytes[0] != 127
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 192 && bytes[1] == 168)
                && bytes[0] < 224;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            byte[] bytes = address.GetAddressBytes();
            return !address.Equals(IPAddress.IPv6Any)
                && !address.Equals(IPAddress.IPv6Loopback)
                && !address.IsIPv6LinkLocal
                && !address.IsIPv6Multicast
                && (bytes[0] & 0xFE) != 0xFC;
        }

        return false;
    }

    private sealed class TransientFeedHttpException : HttpRequestException
    {
        public TransientFeedHttpException(
            HttpStatusCode statusCode,
            TimeSpan? retryAfter)
            : base(
                $"Feed request returned transient HTTP {(int)statusCode}.",
                null,
                statusCode)
        {
            RetryAfter = retryAfter;
        }

        public TimeSpan? RetryAfter { get; }
    }
}
