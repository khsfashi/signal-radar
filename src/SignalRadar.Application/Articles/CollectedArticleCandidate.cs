namespace SignalRadar.Application.Articles;

public sealed record CollectedArticleCandidate(
    string Title,
    string Url,
    string Source,
    DateTimeOffset PublishedAt);
