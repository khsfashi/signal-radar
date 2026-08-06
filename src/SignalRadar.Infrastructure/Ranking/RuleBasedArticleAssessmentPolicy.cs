using SignalRadar.Application.Articles;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Infrastructure.Ranking;

public sealed class RuleBasedArticleAssessmentPolicy : IArticleAssessmentPolicy
{
    private readonly ArticleRankingProfile _profile;

    public RuleBasedArticleAssessmentPolicy(ArticleRankingProfile profile)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public ArticleAssessment Assess(
        CollectedArticleCandidate candidate,
        Uri canonicalUrl,
        DateTimeOffset collectedAt)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(canonicalUrl);

        string searchableText = string.Concat(
            candidate.Title,
            " ",
            candidate.Source,
            " ",
            canonicalUrl.Host).ToLowerInvariant();
        int sourceTrust = CalculateSourceTrust(searchableText);
        (ArticleTopic topics, ArticleTopic primaryTopic) = ClassifyTopics(
            searchableText);
        int topicInterest = CalculateTopicInterest(topics);
        int practicalImpact = CalculatePracticalImpact(searchableText);
        int freshness = CalculateFreshness(candidate.PublishedAt, collectedAt);
        decimal baseScore =
            sourceTrust * 0.30m
            + topicInterest * 0.30m
            + practicalImpact * 0.20m
            + freshness * 0.20m;

        return new ArticleAssessment(
            topics,
            primaryTopic,
            sourceTrust,
            topicInterest,
            practicalImpact,
            freshness,
            baseScore,
            _profile.Version);
    }

    private int CalculateSourceTrust(string searchableText)
    {
        int score = _profile.DefaultSourceTrust;

        for (int index = 0; index < _profile.SourceTrustRules.Count; index++)
        {
            SourceTrustRule rule = _profile.SourceTrustRules[index];

            if (searchableText.Contains(
                    rule.Contains,
                    StringComparison.OrdinalIgnoreCase)
                && rule.Score > score)
            {
                score = rule.Score;
            }
        }

        return score;
    }

    private (ArticleTopic Topics, ArticleTopic PrimaryTopic) ClassifyTopics(
        string searchableText)
    {
        ArticleTopic topics = ArticleTopic.None;
        ArticleTopic primaryTopic = ArticleTopic.Other;
        int primaryPriority = int.MinValue;

        for (int ruleIndex = 0;
            ruleIndex < _profile.TopicRules.Count;
            ruleIndex++)
        {
            TopicKeywordRule rule = _profile.TopicRules[ruleIndex];
            bool matched = false;

            for (int keywordIndex = 0;
                keywordIndex < rule.Keywords.Count;
                keywordIndex++)
            {
                if (ContainsKeyword(searchableText, rule.Keywords[keywordIndex]))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                continue;
            }

            topics |= rule.Topic;

            if (rule.Priority > primaryPriority)
            {
                primaryPriority = rule.Priority;
                primaryTopic = rule.Topic;
            }
        }

        if (topics == ArticleTopic.None)
        {
            return (ArticleTopic.Other, ArticleTopic.Other);
        }

        return (topics, primaryTopic);
    }

    private int CalculateTopicInterest(ArticleTopic topics)
    {
        int score = _profile.DefaultTopicInterest;

        for (int index = 0;
            index < _profile.TopicInterestRules.Count;
            index++)
        {
            TopicInterestRule rule = _profile.TopicInterestRules[index];

            if ((topics & rule.Topic) != ArticleTopic.None
                && rule.Score > score)
            {
                score = rule.Score;
            }
        }

        return score;
    }

    private int CalculatePracticalImpact(string searchableText)
    {
        int score = _profile.BasePracticalImpact;

        for (int index = 0; index < _profile.ImpactRules.Count; index++)
        {
            ImpactKeywordRule rule = _profile.ImpactRules[index];

            if (ContainsKeyword(
                searchableText,
                rule.Contains.ToLowerInvariant()))
            {
                score += rule.Weight;
            }
        }

        return Math.Clamp(score, 0, 100);
    }

    private static int CalculateFreshness(
        DateTimeOffset publishedAt,
        DateTimeOffset collectedAt)
    {
        TimeSpan age = collectedAt.ToUniversalTime()
            - publishedAt.ToUniversalTime();

        if (age < TimeSpan.FromMinutes(-15))
        {
            return 50;
        }

        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age <= TimeSpan.FromHours(6))
        {
            return 100;
        }

        if (age <= TimeSpan.FromDays(1))
        {
            return 90;
        }

        if (age <= TimeSpan.FromDays(3))
        {
            return 75;
        }

        if (age <= TimeSpan.FromDays(7))
        {
            return 55;
        }

        if (age <= TimeSpan.FromDays(30))
        {
            return 30;
        }

        return 10;
    }

    private static bool ContainsKeyword(string text, string keyword)
    {
        if (keyword.Length > 3 || !ContainsOnlyLettersOrDigits(keyword))
        {
            return text.Contains(keyword, StringComparison.Ordinal);
        }

        int startIndex = 0;

        while (startIndex < text.Length)
        {
            int matchIndex = text.IndexOf(
                keyword,
                startIndex,
                StringComparison.Ordinal);

            if (matchIndex < 0)
            {
                return false;
            }

            int endIndex = matchIndex + keyword.Length;
            bool validStart = matchIndex == 0
                || !char.IsLetterOrDigit(text[matchIndex - 1]);
            bool validEnd = endIndex == text.Length
                || !char.IsLetterOrDigit(text[endIndex]);

            if (validStart && validEnd)
            {
                return true;
            }

            startIndex = matchIndex + 1;
        }

        return false;
    }

    private static bool ContainsOnlyLettersOrDigits(string text)
    {
        for (int index = 0; index < text.Length; index++)
        {
            if (!char.IsLetterOrDigit(text[index]))
            {
                return false;
            }
        }

        return true;
    }
}
