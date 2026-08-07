using SignalRadar.Domain.Articles;

namespace SignalRadar.Application.Articles;

public enum CollectArticleStatus
{
    Added = 0,
    Duplicate = 1
}

public sealed record CollectArticleResult(
    Article Article,
    CollectArticleStatus Status);
