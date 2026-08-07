using SignalRadar.Application.Articles;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Infrastructure.Ranking;

public sealed class RuleBasedArticleAssessmentPolicy : IArticleAssessmentPolicy
{
    private static readonly string[] EconomyKeywords =
    [
        "inflation", "interest rate", "central bank", "gdp", "cpi",
        "unemployment", "exchange rate", "macroeconomic", "economy",
        "한국은행", "기준금리", "금리", "물가", "환율", "고용", "경제성장", "경제"
    ];

    private static readonly string[] MarketKeywords =
    [
        "stock market", "nasdaq", "s&p 500", "dow jones", "kospi", "kosdaq",
        "share price", "earnings call", "market cap", "주가", "증시", "코스피",
        "코스닥", "목표주가", "시가총액", "상한가", "하한가"
    ];

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

        string sourceIdentityText = string.Concat(
            candidate.Source,
            " ",
            canonicalUrl.Host).ToLowerInvariant();
        string topicText = string.Concat(
            candidate.Title,
            " ",
            candidate.Source).ToLowerInvariant();
        string impactText = candidate.Title.ToLowerInvariant();
        int sourceTrust = CalculateSourceTrust(sourceIdentityText);
        (ArticleTopic topics, ArticleTopic primaryTopic) = ClassifyTopics(
            topicText);
        int topicInterest = CalculateTopicInterest(topics);
        int practicalImpact = CalculatePracticalImpact(impactText);
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

        AddBuiltInTopic(
            searchableText,
            EconomyKeywords,
            ArticleTopic.Economy,
            priority: 58,
            ref topics,
            ref primaryTopic,
            ref primaryPriority);
        AddBuiltInTopic(
            searchableText,
            MarketKeywords,
            ArticleTopic.Markets,
            priority: 68,
            ref topics,
            ref primaryTopic,
            ref primaryPriority);

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

        if ((topics & ArticleTopic.Markets) != ArticleTopic.None)
        {
            score = Math.Max(score, 70);
        }

        if ((topics & ArticleTopic.Economy) != ArticleTopic.None)
        {
            score = Math.Max(score, 65);
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

    private static void AddBuiltInTopic(
        string searchableText,
        IReadOnlyList<string> keywords,
        ArticleTopic topic,
        int priority,
        ref ArticleTopic topics,
        ref ArticleTopic primaryTopic,
        ref int primaryPriority)
    {
        for (int index = 0; index < keywords.Count; index++)
        {
            if (!ContainsKeyword(searchableText, keywords[index]))
            {
                continue;
            }

            topics |= topic;

            if (priority > primaryPriority)
            {
                primaryPriority = priority;
                primaryTopic = topic;
            }

            return;
        }
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
