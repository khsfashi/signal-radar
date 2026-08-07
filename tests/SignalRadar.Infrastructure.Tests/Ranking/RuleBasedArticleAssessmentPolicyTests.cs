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

    [Fact]
    public void Assess_ClassifiesKoreanMacroeconomyWithoutProfileMigration()
    {
        RuleBasedArticleAssessmentPolicy policy = new(
            ArticleRankingProfile.CreateDefault());
        DateTimeOffset collectedAt = DateTimeOffset.UtcNow;
        CollectedArticleCandidate candidate = new(
            "한국은행 기준금리 동결, 환율과 물가 전망 점검",
            "https://example.com/bok-rate",
            "economy - Bank of Korea press releases",
            collectedAt.AddMinutes(-30));

        ArticleAssessment assessment = policy.Assess(
            candidate,
            new Uri(candidate.Url),
            collectedAt);

        Assert.True(assessment.Topics.HasFlag(ArticleTopic.Economy));
        Assert.Equal(ArticleTopic.Economy, assessment.PrimaryTopic);
        Assert.True(assessment.TopicInterest >= 65);
    }

    [Fact]
    public void Assess_ClassifiesKoreanStockMarketWithoutProfileMigration()
    {
        RuleBasedArticleAssessmentPolicy policy = new(
            ArticleRankingProfile.CreateDefault());
        DateTimeOffset collectedAt = DateTimeOffset.UtcNow;
        CollectedArticleCandidate candidate = new(
            "코스피 상승 마감, 반도체 주가 강세",
            "https://example.com/kospi-close",
            "stock market korea - Google News",
            collectedAt.AddMinutes(-20));

        ArticleAssessment assessment = policy.Assess(
            candidate,
            new Uri(candidate.Url),
            collectedAt);

        Assert.True(assessment.Topics.HasFlag(ArticleTopic.Markets));
        Assert.Equal(ArticleTopic.Markets, assessment.PrimaryTopic);
        Assert.True(assessment.TopicInterest >= 70);
    }
}
