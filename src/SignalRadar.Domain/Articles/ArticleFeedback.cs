namespace SignalRadar.Domain.Articles;

public enum ArticleFeedbackKind
{
    Interested = 3,
    NotInterested = -2,
    Hidden = -3
}

public sealed record ArticleFeedbackAggregate(
    int FeedbackCount,
    int Weight)
{
    public decimal ScoreAdjustment => Math.Clamp(Weight * 5m, -30m, 30m);
}

public static class ArticleFeedbackWeights
{
    public static int GetWeight(ArticleFeedbackKind kind)
    {
        return kind switch
        {
            ArticleFeedbackKind.Interested => 3,
            ArticleFeedbackKind.NotInterested => -2,
            ArticleFeedbackKind.Hidden => -3,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
