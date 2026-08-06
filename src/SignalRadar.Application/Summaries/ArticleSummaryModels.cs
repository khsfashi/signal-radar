namespace SignalRadar.Application.Summaries;

public sealed record ArticleSummaryContent(
    string Title,
    string Overview,
    IReadOnlyList<string> KeyPoints,
    string WhyItMatters,
    IReadOnlyList<string> WatchNext,
    IReadOnlyList<string> Caveats);

public sealed record ArticleSummaryProviderRequest(
    string Instructions,
    string Input,
    string Language);

public sealed record ArticleSummaryProviderResponse(
    ArticleSummaryContent Content,
    string? ProviderResponseId = null);

public sealed record ArticleSummaryCacheEntry(
    string InputHash,
    string Provider,
    string Model,
    string PromptVersion,
    string Language,
    int ArticleCount,
    ArticleSummaryContent Content,
    DateTimeOffset GeneratedAt,
    string? ProviderResponseId = null);

public sealed record GeneratedArticleSummary(
    ArticleSummaryCacheEntry Summary,
    bool CacheHit);

public interface IArticleSummaryProvider
{
    public string ProviderName { get; }

    public string ModelName { get; }

    public ValueTask<ArticleSummaryProviderResponse> GenerateAsync(
        ArticleSummaryProviderRequest request,
        CancellationToken cancellationToken);
}

public interface IArticleSummaryCache
{
    public ValueTask<ArticleSummaryCacheEntry?> TryGetAsync(
        string inputHash,
        CancellationToken cancellationToken);

    public ValueTask StoreAsync(
        ArticleSummaryCacheEntry entry,
        CancellationToken cancellationToken);
}
