using SignalRadar.Domain.Articles;

namespace SignalRadar.Application.Articles;

public interface IArticleInbox
{
    ValueTask<bool> TryAddAsync(Article article, CancellationToken cancellationToken);
}
