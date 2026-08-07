using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using SignalRadar.Application.Articles;
using SignalRadar.Application.Summaries;

namespace SignalRadar.Infrastructure.Summaries;

public sealed class HttpArticleContentReader : IArticleContentReader
{
    private const string UserAgent =
        "SignalRadarBot/0.1 (+https://github.com/khsfashi/signal-radar)";
    private readonly HttpClient _httpClient;
    private readonly IArticleContentCache _cache;
    private readonly ArticleContentHttpOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly PublicHttpTargetValidator _targetValidator;
    private readonly RobotsPolicyClient _robotsPolicyClient;
    private readonly HtmlArticleTextExtractor _textExtractor;

    public HttpArticleContentReader(
        HttpClient httpClient,
        IArticleContentCache cache,
        ArticleContentHttpOptions options,
        TimeProvider timeProvider)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _targetValidator = new PublicHttpTargetValidator(
            options.AllowPrivateNetworkTargets);
        _robotsPolicyClient = new RobotsPolicyClient(
            httpClient,
            options,
            _targetValidator,
            timeProvider);
        _textExtractor = new HtmlArticleTextExtractor(
            options.MinimumExtractedCharacters,
            options.MaximumExtractedCharacters);
    }

    public async ValueTask<ArticleContentSnapshot> GetAsync(
        SavedArticle article,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(article);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        ArticleContentSnapshot? cached = await _cache.TryGetFreshAsync(
            article.ArticleId,
            article.CanonicalUrl,
            now,
            cancellationToken).ConfigureAwait(false);

        if (cached is not null)
        {
            return cached;
        }

        ArticleContentSnapshot fetched = await FetchAsync(
            article,
            now,
            cancellationToken).ConfigureAwait(false);
        await _cache.StoreAsync(fetched, cancellationToken).ConfigureAwait(false);
        return fetched;
    }

    private async ValueTask<ArticleContentSnapshot> FetchAsync(
        SavedArticle article,
        DateTimeOffset fetchedAt,
        CancellationToken cancellationToken)
    {
        Uri requestUri = article.CanonicalUrl;

        for (int redirectCount = 0; ; redirectCount++)
        {
            try
            {
                await _targetValidator
                    .ValidateAsync(requestUri, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return CreateFailure(
                    article,
                    ArticleContentStatus.Unavailable,
                    fetchedAt,
                    null,
                    null,
                    exception.Message);
            }

            RobotsAccessDecision robotsDecision = await _robotsPolicyClient
                .CanFetchAsync(requestUri, cancellationToken)
                .ConfigureAwait(false);

            if (!robotsDecision.Allowed)
            {
                return CreateFailure(
                    article,
                    ArticleContentStatus.RobotsDisallowed,
                    fetchedAt,
                    null,
                    null,
                    robotsDecision.Detail);
            }

            using HttpRequestMessage request = new(HttpMethod.Get, requestUri);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.ParseAdd(
                "text/html, application/xhtml+xml;q=0.9");
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.RequestTimeout);

            HttpResponseMessage response;

            try
            {
                response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return CreateFailure(
                    article,
                    ArticleContentStatus.Unavailable,
                    fetchedAt,
                    null,
                    null,
                    $"Article request exceeded {_options.RequestTimeout}.");
            }
            catch (Exception exception)
            {
                return CreateFailure(
                    article,
                    ArticleContentStatus.Unavailable,
                    fetchedAt,
                    null,
                    null,
                    exception.Message);
            }

            using (response)
            {
                int statusCode = (int)response.StatusCode;

                if (IsRedirect(response.StatusCode))
                {
                    if (redirectCount >= _options.MaximumRedirects
                        || response.Headers.Location is null)
                    {
                        return CreateFailure(
                            article,
                            ArticleContentStatus.HttpError,
                            fetchedAt,
                            statusCode,
                            null,
                            "The article exceeded the redirect limit or returned no Location header.");
                    }

                    requestUri = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(requestUri, response.Headers.Location);
                    continue;
                }

                string? contentType = NormalizeContentType(
                    response.Content?.Headers.ContentType);

                if (!response.IsSuccessStatusCode)
                {
                    return CreateFailure(
                        article,
                        ArticleContentStatus.HttpError,
                        fetchedAt,
                        statusCode,
                        contentType,
                        $"Article request returned HTTP {statusCode}.");
                }

                if (!IsSupportedContentType(contentType))
                {
                    return CreateFailure(
                        article,
                        ArticleContentStatus.UnsupportedContentType,
                        fetchedAt,
                        statusCode,
                        contentType,
                        "Article response was not HTML or XHTML.");
                }

                HttpContent content = response.Content
                    ?? throw new InvalidDataException(
                        "The article response did not contain a body.");
                BoundedContentResult body = await ReadBoundedContentAsync(
                    content,
                    timeout.Token).ConfigureAwait(false);

                if (body.TooLarge)
                {
                    return CreateFailure(
                        article,
                        ArticleContentStatus.TooLarge,
                        fetchedAt,
                        statusCode,
                        contentType,
                        "Article HTML exceeded the configured response-size limit.");
                }

                string html = DecodeHtml(body.Bytes, content.Headers.ContentType);
                HtmlArticleText? extracted = await _textExtractor.ExtractAsync(
                    html,
                    cancellationToken).ConfigureAwait(false);

                if (extracted is null)
                {
                    return CreateFailure(
                        article,
                        ArticleContentStatus.TooShort,
                        fetchedAt,
                        statusCode,
                        contentType,
                        "No sufficiently long article-like text block was found.");
                }

                string hash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(extracted.Text)));
                string detail = requestUri == article.CanonicalUrl
                    ? "Article text extracted from the canonical URL."
                    : $"Article text extracted after redirect to {requestUri.Host}.";
                return new ArticleContentSnapshot(
                    article.ArticleId,
                    article.CanonicalUrl,
                    ArticleContentStatus.Extracted,
                    extracted.Text,
                    hash,
                    fetchedAt,
                    fetchedAt.Add(_options.SuccessCacheDuration),
                    statusCode,
                    contentType,
                    NormalizeDetail(detail));
            }
        }
    }

    private ArticleContentSnapshot CreateFailure(
        SavedArticle article,
        ArticleContentStatus status,
        DateTimeOffset fetchedAt,
        int? httpStatusCode,
        string? contentType,
        string detail)
    {
        TimeSpan cacheDuration = status switch
        {
            ArticleContentStatus.RobotsDisallowed => _options.RobotsCacheDuration,
            ArticleContentStatus.UnsupportedContentType
                or ArticleContentStatus.TooLarge
                or ArticleContentStatus.TooShort => _options.SuccessCacheDuration,
            ArticleContentStatus.HttpError when httpStatusCode is >= 400 and <= 499
                => _options.SuccessCacheDuration,
            _ => _options.UnavailableCacheDuration
        };
        return new ArticleContentSnapshot(
            article.ArticleId,
            article.CanonicalUrl,
            status,
            null,
            null,
            fetchedAt,
            fetchedAt.Add(cacheDuration),
            httpStatusCode,
            contentType,
            NormalizeDetail(detail));
    }

    private async ValueTask<BoundedContentResult> ReadBoundedContentAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > _options.MaximumResponseBytes)
        {
            return new BoundedContentResult([], TooLarge: true);
        }

        int initialCapacity = content.Headers.ContentLength.HasValue
            ? checked((int)content.Headers.ContentLength.Value)
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
                int read = await input.ReadAsync(
                    buffer.AsMemory(0, buffer.Length),
                    cancellationToken).ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                totalRead += read;

                if (totalRead > _options.MaximumResponseBytes)
                {
                    return new BoundedContentResult([], TooLarge: true);
                }

                await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return new BoundedContentResult(output.ToArray(), TooLarge: false);
    }

    private static string DecodeHtml(
        byte[] bytes,
        MediaTypeHeaderValue? contentType)
    {
        string? charset = contentType?.CharSet?.Trim().Trim('"', '\'');
        Encoding encoding = Encoding.UTF8;

        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                encoding = Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
            }
        }

        return encoding.GetString(bytes);
    }

    private static string? NormalizeContentType(MediaTypeHeaderValue? contentType)
    {
        string? mediaType = contentType?.MediaType;
        return string.IsNullOrWhiteSpace(mediaType)
            ? null
            : mediaType.Trim().ToLowerInvariant();
    }

    private static bool IsSupportedContentType(string? contentType)
    {
        return contentType is "text/html" or "application/xhtml+xml";
    }

    private static string NormalizeDetail(string value)
    {
        string normalized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
    }

    private sealed record BoundedContentResult(byte[] Bytes, bool TooLarge);
}
