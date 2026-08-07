namespace SignalRadar.Application.Articles;

public sealed record ArticleReclassificationResult(
    int ScannedCount,
    int UpdatedCount);

public interface IArticleReclassificationService
{
    public ValueTask<ArticleReclassificationResult> ReclassifyAsync(
        TimeSpan age,
        CancellationToken cancellationToken);
}
