using SignalRadar.Application.Preferences;
using SignalRadar.Application.Translation;

namespace SignalRadar.Application.Articles;

public sealed class SourceFilteredArticleRankingReader : IArticleRankingReader
{
    private const int ExpandedTopLimit = 100;
    private const int ExpandedSearchLimit = 25;
    private readonly IArticleRankingReader _inner;
    private readonly ISourcePreferenceStore _preferences;

    public SourceFilteredArticleRankingReader(
        IArticleRankingReader inner,
        ISourcePreferenceStore preferences)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _preferences = preferences
            ?? throw new ArgumentNullException(nameof(preferences));
    }

    public async ValueTask<IReadOnlyList<RankedArticle>> GetTopAsync(
        ArticleRankingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlySet<string> muted = await GetMutedSourcesAsync(
            query.ActorId,
            cancellationToken).ConfigureAwait(false);

        if (muted.Count == 0)
        {
            return await _inner.GetTopAsync(query, cancellationToken)
                .ConfigureAwait(false);
        }

        ArticleRankingQuery expanded = query with
        {
            Limit = Math.Max(query.Limit, ExpandedTopLimit)
        };
        IReadOnlyList<RankedArticle> candidates = await _inner
            .GetTopAsync(expanded, cancellationToken)
            .ConfigureAwait(false);
        return Filter(candidates, muted, query.Limit);
    }

    public async ValueTask<IReadOnlyList<RankedArticle>> SearchAsync(
        ArticleSearchQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        IReadOnlySet<string> muted = await GetMutedSourcesAsync(
            query.ActorId,
            cancellationToken).ConfigureAwait(false);

        if (muted.Count == 0)
        {
            return await _inner.SearchAsync(query, cancellationToken)
                .ConfigureAwait(false);
        }

        ArticleSearchQuery expanded = query with
        {
            Limit = Math.Max(query.Limit, ExpandedSearchLimit)
        };
        IReadOnlyList<RankedArticle> candidates = await _inner
            .SearchAsync(expanded, cancellationToken)
            .ConfigureAwait(false);
        return Filter(candidates, muted, query.Limit);
    }

    private async ValueTask<IReadOnlySet<string>> GetMutedSourcesAsync(
        string? actorId,
        CancellationToken cancellationToken)
    {
        return string.IsNullOrWhiteSpace(actorId)
            ? EmptySourceSet.Instance
            : await _preferences.GetMutedSourcesAsync(
                actorId,
                cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<RankedArticle> Filter(
        IReadOnlyList<RankedArticle> candidates,
        IReadOnlySet<string> muted,
        int limit)
    {
        List<RankedArticle> filtered = new(Math.Min(limit, candidates.Count));

        for (int index = 0;
            index < candidates.Count && filtered.Count < limit;
            index++)
        {
            RankedArticle article = candidates[index];

            if (!muted.Contains(article.Source))
            {
                filtered.Add(article);
            }
        }

        return filtered;
    }

    private sealed class EmptySourceSet : IReadOnlySet<string>
    {
        public static EmptySourceSet Instance { get; } = new();
        public int Count => 0;
        public bool Contains(string item) => false;
        public IEnumerator<string> GetEnumerator() =>
            Enumerable.Empty<string>().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
        public bool IsProperSubsetOf(IEnumerable<string> other) => true;
        public bool IsProperSupersetOf(IEnumerable<string> other) => false;
        public bool IsSubsetOf(IEnumerable<string> other) => true;
        public bool IsSupersetOf(IEnumerable<string> other) => !other.Any();
        public bool Overlaps(IEnumerable<string> other) => false;
        public bool SetEquals(IEnumerable<string> other) => !other.Any();
    }
}

public sealed class TranslatingArticleRankingReader : IArticleRankingReader
{
    private readonly IArticleRankingReader _inner;
    private readonly ITitleTranslator _translator;

    public TranslatingArticleRankingReader(
        IArticleRankingReader inner,
        ITitleTranslator translator)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _translator = translator
            ?? throw new ArgumentNullException(nameof(translator));
    }

    public async ValueTask<IReadOnlyList<RankedArticle>> GetTopAsync(
        ArticleRankingQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RankedArticle> articles = await _inner
            .GetTopAsync(query, cancellationToken).ConfigureAwait(false);
        return await TranslateAsync(articles, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<RankedArticle>> SearchAsync(
        ArticleSearchQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RankedArticle> articles = await _inner
            .SearchAsync(query, cancellationToken).ConfigureAwait(false);
        return await TranslateAsync(articles, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IReadOnlyList<RankedArticle>> TranslateAsync(
        IReadOnlyList<RankedArticle> articles,
        CancellationToken cancellationToken)
    {
        if (articles.Count == 0)
        {
            return articles;
        }

        RankedArticle[] translated = new RankedArticle[articles.Count];

        for (int index = 0; index < articles.Count; index++)
        {
            RankedArticle article = articles[index];
            string title = await _translator.TranslateAsync(
                article.Title,
                cancellationToken).ConfigureAwait(false);
            translated[index] = article with { Title = title };
        }

        return translated;
    }
}

public sealed class TranslatingArticleSavedReader : IArticleSavedReader
{
    private readonly IArticleSavedReader _inner;
    private readonly ITitleTranslator _translator;

    public TranslatingArticleSavedReader(
        IArticleSavedReader inner,
        ITitleTranslator translator)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _translator = translator
            ?? throw new ArgumentNullException(nameof(translator));
    }

    public async ValueTask<IReadOnlyList<SavedArticle>> GetSavedAsync(
        SavedArticleQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SavedArticle> articles = await _inner
            .GetSavedAsync(query, cancellationToken).ConfigureAwait(false);

        if (articles.Count == 0)
        {
            return articles;
        }

        SavedArticle[] translated = new SavedArticle[articles.Count];

        for (int index = 0; index < articles.Count; index++)
        {
            SavedArticle article = articles[index];
            string title = await _translator.TranslateAsync(
                article.Title,
                cancellationToken).ConfigureAwait(false);
            translated[index] = article with { Title = title };
        }

        return translated;
    }
}
