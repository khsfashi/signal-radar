using SignalRadar.Domain.Articles;

namespace SignalRadar.Application.Articles;

public sealed class CollectArticleUseCase
{
    private readonly IArticleInbox _articleInbox;
    private readonly IUrlCanonicalizer _urlCanonicalizer;
    private readonly IArticleAssessmentPolicy _assessmentPolicy;
    private readonly TimeProvider _timeProvider;

    public CollectArticleUseCase(
        IArticleInbox articleInbox,
        IUrlCanonicalizer urlCanonicalizer,
        IArticleAssessmentPolicy assessmentPolicy,
        TimeProvider timeProvider)
    {
        _articleInbox = articleInbox
            ?? throw new ArgumentNullException(nameof(articleInbox));
        _urlCanonicalizer = urlCanonicalizer
            ?? throw new ArgumentNullException(nameof(urlCanonicalizer));
        _assessmentPolicy = assessmentPolicy
            ?? throw new ArgumentNullException(nameof(assessmentPolicy));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async ValueTask<CollectArticleResult> ExecuteAsync(
        CollectedArticleCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        Uri canonicalUrl = _urlCanonicalizer.Normalize(candidate.Url);
        DateTimeOffset collectedAt = _timeProvider.GetUtcNow();
        ArticleAssessment assessment = _assessmentPolicy.Assess(
            candidate,
            canonicalUrl,
            collectedAt);
        Article article = Article.Create(
            candidate.Title,
            canonicalUrl,
            candidate.Source,
            candidate.PublishedAt,
            collectedAt,
            candidate.ExternalId,
            assessment);

        bool wasAdded = await _articleInbox
            .TryAddAsync(article, cancellationToken)
            .ConfigureAwait(false);

        CollectArticleStatus status = wasAdded
            ? CollectArticleStatus.Added
            : CollectArticleStatus.Duplicate;

        return new CollectArticleResult(article, status);
    }
}
