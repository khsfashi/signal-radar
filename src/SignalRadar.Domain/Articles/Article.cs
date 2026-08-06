namespace SignalRadar.Domain.Articles;

public sealed class Article
{
    private Article(
        Guid id,
        string title,
        Uri canonicalUrl,
        string source,
        string? externalId,
        DateTimeOffset publishedAt,
        DateTimeOffset collectedAt,
        ArticleAssessment assessment)
    {
        Id = id;
        Title = title;
        CanonicalUrl = canonicalUrl;
        Source = source;
        ExternalId = externalId;
        PublishedAt = publishedAt;
        CollectedAt = collectedAt;
        Assessment = assessment;
    }

    public Guid Id { get; }

    public string Title { get; }

    public Uri CanonicalUrl { get; }

    public string Source { get; }

    public string? ExternalId { get; }

    public DateTimeOffset PublishedAt { get; }

    public DateTimeOffset CollectedAt { get; }

    public ArticleAssessment Assessment { get; }

    public static Article Create(
        string title,
        Uri canonicalUrl,
        string source,
        DateTimeOffset publishedAt,
        DateTimeOffset collectedAt,
        string? externalId = null,
        ArticleAssessment? assessment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(canonicalUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (!canonicalUrl.IsAbsoluteUri)
        {
            throw new ArgumentException(
                "The canonical URL must be absolute.",
                nameof(canonicalUrl));
        }

        if (canonicalUrl.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "Only HTTP and HTTPS article URLs are supported.",
                nameof(canonicalUrl));
        }

        string? normalizedExternalId = NormalizeExternalId(externalId);

        return new Article(
            Guid.NewGuid(),
            title.Trim(),
            canonicalUrl,
            source.Trim(),
            normalizedExternalId,
            publishedAt.ToUniversalTime(),
            collectedAt.ToUniversalTime(),
            assessment ?? ArticleAssessment.Unclassified);
    }

    private static string? NormalizeExternalId(string? externalId)
    {
        if (externalId is null)
        {
            return null;
        }

        string normalized = externalId.Trim();

        if (normalized.Length is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(externalId),
                "An external identifier must contain between 1 and 500 characters.");
        }

        return normalized;
    }
}
