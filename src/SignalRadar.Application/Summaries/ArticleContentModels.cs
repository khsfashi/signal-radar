using SignalRadar.Application.Articles;

namespace SignalRadar.Application.Summaries;

public enum ArticleContentStatus : short
{
    Extracted = 1,
    RobotsDisallowed = 2,
    UnsupportedContentType = 3,
    TooLarge = 4,
    TooShort = 5,
    HttpError = 6,
    Unavailable = 7
}

public sealed record ArticleContentSnapshot(
    Guid ArticleId,
    Uri CanonicalUrl,
    ArticleContentStatus Status,
    string? Text,
    string? ContentHash,
    DateTimeOffset FetchedAt,
    DateTimeOffset RefreshAfter,
    int? HttpStatusCode = null,
    string? ContentType = null,
    string? Detail = null)
{
    public bool HasExtractedContent =>
        Status == ArticleContentStatus.Extracted
        && !string.IsNullOrWhiteSpace(Text)
        && !string.IsNullOrWhiteSpace(ContentHash);
}

public interface IArticleContentReader
{
    public ValueTask<ArticleContentSnapshot> GetAsync(
        SavedArticle article,
        CancellationToken cancellationToken);
}

public interface IArticleContentCache
{
    public ValueTask<ArticleContentSnapshot?> TryGetFreshAsync(
        Guid articleId,
        Uri canonicalUrl,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    public ValueTask StoreAsync(
        ArticleContentSnapshot snapshot,
        CancellationToken cancellationToken);
}
