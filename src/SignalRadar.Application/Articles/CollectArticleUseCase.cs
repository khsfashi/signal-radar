using SignalRadar.Domain.Articles;

namespace SignalRadar.Application.Articles;

public sealed class CollectArticleUseCase
{
    private readonly IArticleInbox _articleInbox;
    private readonly IUrlCanonicalizer _urlCanonicalizer;
    private readonly TimeProvider _timeProvider;

    public CollectArticleUseCase(
        IArticleInbox articleInbox,
        IUrlCanonicalizer urlCanonicalizer,
        TimeProvider timeProvider)
    {
        _articleInbox = articleInbox;
        _urlCanonicalizer = urlCanonicalizer;
        _timeProvider = timeProvider;
    }

    public async ValueTask<CollectArticleResult> ExecuteAsync(
        CollectedArticleCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        Uri canonicalUrl = _urlCanonicalizer.Normalize(candidate.Url);
        Article article = Article.Create(
            candidate.Title,
            canonicalUrl,
            candidate.Source,
            candidate.PublishedAt,
            _timeProvider.GetUtcNow(),
            candidate.ExternalId);

        bool wasAdded = await _articleInbox
            .TryAddAsync(article, cancellationToken)
            .ConfigureAwait(false);

        CollectArticleStatus status = wasAdded
            ? CollectArticleStatus.Added
            : CollectArticleStatus.Duplicate;

        return new CollectArticleResult(article, status);
    }
}
