namespace SignalRadar.Domain.Articles;

public sealed class Article
{
    private Article(
        Guid id,
        string title,
        Uri canonicalUrl,
        string source,
        DateTimeOffset publishedAt,
        DateTimeOffset collectedAt)
    {
        Id = id;
        Title = title;
        CanonicalUrl = canonicalUrl;
        Source = source;
        PublishedAt = publishedAt;
        CollectedAt = collectedAt;
    }

    public Guid Id { get; }

    public string Title { get; }

    public Uri CanonicalUrl { get; }

    public string Source { get; }

    public DateTimeOffset PublishedAt { get; }

    public DateTimeOffset CollectedAt { get; }

    public static Article Create(
        string title,
        Uri canonicalUrl,
        string source,
        DateTimeOffset publishedAt,
        DateTimeOffset collectedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(canonicalUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (!canonicalUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("The canonical URL must be absolute.", nameof(canonicalUrl));
        }

        if (canonicalUrl.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Only HTTP and HTTPS article URLs are supported.", nameof(canonicalUrl));
        }

        return new Article(
            Guid.NewGuid(),
            title.Trim(),
            canonicalUrl,
            source.Trim(),
            publishedAt.ToUniversalTime(),
            collectedAt.ToUniversalTime());
    }
}
