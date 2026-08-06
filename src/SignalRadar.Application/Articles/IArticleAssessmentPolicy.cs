using SignalRadar.Domain.Articles;

namespace SignalRadar.Application.Articles;

public interface IArticleAssessmentPolicy
{
    public ArticleAssessment Assess(
        CollectedArticleCandidate candidate,
        Uri canonicalUrl,
        DateTimeOffset collectedAt);
}
