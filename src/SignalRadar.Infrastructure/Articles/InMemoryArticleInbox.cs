using SignalRadar.Application.Articles;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Infrastructure.Articles;

public sealed class InMemoryArticleInbox : IArticleInbox
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Article> _articlesByUrl =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _externalKeys = new(StringComparer.Ordinal);

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _articlesByUrl.Count;
            }
        }
    }

    public ValueTask<bool> TryAddAsync(
        Article article,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(article);
        cancellationToken.ThrowIfCancellationRequested();

        string canonicalUrl = article.CanonicalUrl.AbsoluteUri;
        string? externalKey = article.ExternalId is null
            ? null
            : CreateExternalKey(article.Source, article.ExternalId);

        lock (_gate)
        {
            if (_articlesByUrl.ContainsKey(canonicalUrl)
                || externalKey is not null && _externalKeys.Contains(externalKey))
            {
                return ValueTask.FromResult(false);
            }

            _articlesByUrl.Add(canonicalUrl, article);

            if (externalKey is not null)
            {
                _externalKeys.Add(externalKey);
            }

            return ValueTask.FromResult(true);
        }
    }

    public IReadOnlyCollection<Article> Snapshot()
    {
        lock (_gate)
        {
            return _articlesByUrl.Values.ToArray();
        }
    }

    private static string CreateExternalKey(string source, string externalId)
    {
        return string.Concat(source, "\n", externalId);
    }
}
