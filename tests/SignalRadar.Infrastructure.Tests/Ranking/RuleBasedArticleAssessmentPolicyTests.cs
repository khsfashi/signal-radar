using SignalRadar.Application.Articles;
using SignalRadar.Domain.Articles;
using SignalRadar.Infrastructure.Ranking;
using Xunit;

namespace SignalRadar.Infrastructure.Tests.Ranking;

public sealed class RuleBasedArticleAssessmentPolicyTests
{
    [Fact]
    public void Assess_ClassifiesMultipleTopicsAndUsesPriorityForPrimaryTopic()
    {
        RuleBasedArticleAssessmentPolicy policy = new(
            ArticleRankingProfile.CreateDefault());
        DateTimeOffset collectedAt = new(
            2026,
            8,
            6,
            10,
            0,
            0,
            TimeSpan.Zero);
        CollectedArticleCandidate candidate = new(
            "OpenAI releases an AI agent plugin for Unreal Engine",
            "https://openai.com/index/unreal-agent",
            "OpenAI official",
            collectedAt.AddHours(-1));

        ArticleAssessment assessment = policy.Assess(
            candidate,
            new Uri(candidate.Url),
            collectedAt);

        Assert.True(
            assessment.Topics.HasFlag(ArticleTopic.ArtificialIntelligence));
        Assert.True(
            assessment.Topics.HasFlag(ArticleTopic.GameDevelopment));
        Assert.Equal(
            ArticleTopic.ArtificialIntelligence,
            assessment.PrimaryTopic);
        Assert.Equal(100, assessment.SourceTrust);
        Assert.Equal(100, assessment.TopicInterest);
        Assert.Equal(100, assessment.Freshness);
        Assert.True(assessment.BaseScore >= 80m);
    }

    [Fact]
    public void Assess_DoesNotMatchShortKeywordInsideAnotherWord()
    {
        RuleBasedArticleAssessmentPolicy policy = new(
            ArticleRankingProfile.CreateDefault());
        DateTimeOffset collectedAt = DateTimeOffset.UtcNow;
        CollectedArticleCandidate candidate = new(
            "A daily database maintenance guide",
            "https://example.com/daily-database",
            "example",
            collectedAt);

        ArticleAssessment assessment = policy.Assess(
            candidate,
            new Uri(candidate.Url),
            collectedAt);

        Assert.False(
            assessment.Topics.HasFlag(ArticleTopic.ArtificialIntelligence));
        Assert.True(
            assessment.Topics.HasFlag(ArticleTopic.DeveloperTools));
    }
}
