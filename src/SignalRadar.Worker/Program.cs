using SignalRadar.Application.Articles;
using SignalRadar.Infrastructure.Articles;

CanonicalUrlNormalizer urlNormalizer = new();
InMemoryArticleInbox articleInbox = new();
CollectArticleUseCase collectArticle = new(
    articleInbox,
    urlNormalizer,
    TimeProvider.System);

CollectedArticleCandidate candidate = new(
    "Signal Radar bootstrap",
    "https://example.com/articles/bootstrap?utm_source=discord&lang=en#section",
    "local-sample",
    DateTimeOffset.UtcNow);

CollectArticleResult result = await collectArticle.ExecuteAsync(candidate);

Console.WriteLine(
    $"Status={result.Status}, CanonicalUrl={result.Article.CanonicalUrl}, InboxCount={articleInbox.Count}");
