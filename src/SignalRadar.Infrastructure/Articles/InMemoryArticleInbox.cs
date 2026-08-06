using System.Collections.Concurrent;
using SignalRadar.Application.Articles;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Infrastructure.Articles;

public sealed class InMemoryArticleInbox : IArticleInbox
{
    private readonly ConcurrentDictionary<string, Article> _articles =
        new(StringComparer.Ordinal);

    public int Count => _articles.Count;

    public ValueTask<bool> TryAddAsync(
        Article article,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(article);
        cancellationToken.ThrowIfCancellationRequested();

        bool wasAdded = _articles.TryAdd(article.CanonicalUrl.AbsoluteUri, article);
        return ValueTask.FromResult(wasAdded);
    }

    public IReadOnlyCollection<Article> Snapshot()
    {
        return _articles.Values.ToArray();
    }
}
