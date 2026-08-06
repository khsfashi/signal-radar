using SignalRadar.Domain.Articles;

namespace SignalRadar.Infrastructure.Ranking;

public sealed record SourceTrustRule(string Contains, int Score);

public sealed record TopicInterestRule(ArticleTopic Topic, int Score);

public sealed class TopicKeywordRule
{
    public TopicKeywordRule(
        ArticleTopic topic,
        int priority,
        IReadOnlyList<string> keywords)
    {
        if (!IsSingleTopic(topic) || topic == ArticleTopic.Other)
        {
            throw new ArgumentOutOfRangeException(nameof(topic));
        }

        if (priority is < 0 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
        }

        ArgumentNullException.ThrowIfNull(keywords);

        if (keywords.Count == 0)
        {
            throw new ArgumentException(
                "A topic rule requires at least one keyword.",
                nameof(keywords));
        }

        string[] normalizedKeywords = new string[keywords.Count];

        for (int index = 0; index < keywords.Count; index++)
        {
            string keyword = keywords[index];
            ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
            normalizedKeywords[index] = keyword.Trim().ToLowerInvariant();
        }

        Topic = topic;
        Priority = priority;
        Keywords = normalizedKeywords;
    }

    public ArticleTopic Topic { get; }

    public int Priority { get; }

    public IReadOnlyList<string> Keywords { get; }

    private static bool IsSingleTopic(ArticleTopic topic)
    {
        int value = (int)topic;
        return value > 0 && (value & (value - 1)) == 0;
    }
}

public sealed record ImpactKeywordRule(string Contains, int Weight);

public sealed class ArticleRankingProfile
{
    public ArticleRankingProfile(
        string version,
        int defaultSourceTrust,
        int defaultTopicInterest,
        int basePracticalImpact,
        IReadOnlyList<SourceTrustRule> sourceTrustRules,
        IReadOnlyList<TopicInterestRule> topicInterestRules,
        IReadOnlyList<TopicKeywordRule> topicRules,
        IReadOnlyList<ImpactKeywordRule> impactRules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ValidateScore(defaultSourceTrust, nameof(defaultSourceTrust));
        ValidateScore(defaultTopicInterest, nameof(defaultTopicInterest));
        ValidateScore(basePracticalImpact, nameof(basePracticalImpact));
        ArgumentNullException.ThrowIfNull(sourceTrustRules);
        ArgumentNullException.ThrowIfNull(topicInterestRules);
        ArgumentNullException.ThrowIfNull(topicRules);
        ArgumentNullException.ThrowIfNull(impactRules);

        string normalizedVersion = version.Trim();

        if (normalizedVersion.Length > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        ValidateSourceTrustRules(sourceTrustRules);
        ValidateTopicInterestRules(topicInterestRules);
        ValidateImpactRules(impactRules);

        Version = normalizedVersion;
        DefaultSourceTrust = defaultSourceTrust;
        DefaultTopicInterest = defaultTopicInterest;
        BasePracticalImpact = basePracticalImpact;
        SourceTrustRules = sourceTrustRules;
        TopicInterestRules = topicInterestRules;
        TopicRules = topicRules;
        ImpactRules = impactRules;
    }

    public string Version { get; }

    public int DefaultSourceTrust { get; }

    public int DefaultTopicInterest { get; }

    public int BasePracticalImpact { get; }

    public IReadOnlyList<SourceTrustRule> SourceTrustRules { get; }

    public IReadOnlyList<TopicInterestRule> TopicInterestRules { get; }

    public IReadOnlyList<TopicKeywordRule> TopicRules { get; }

    public IReadOnlyList<ImpactKeywordRule> ImpactRules { get; }

    public static ArticleRankingProfile CreateDefault()
    {
        return new ArticleRankingProfile(
            "default-v1",
            defaultSourceTrust: 55,
            defaultTopicInterest: 35,
            basePracticalImpact: 35,
            [
                new SourceTrustRule("openai", 100),
                new SourceTrustRule("anthropic", 100),
                new SourceTrustRule("google", 95),
                new SourceTrustRule("microsoft", 95),
                new SourceTrustRule("github", 90),
                new SourceTrustRule("unreal engine", 95),
                new SourceTrustRule("unity", 95),
                new SourceTrustRule("arxiv", 90),
                new SourceTrustRule("geeknews", 78),
                new SourceTrustRule("hacker news", 70)
            ],
            [
                new TopicInterestRule(ArticleTopic.ArtificialIntelligence, 100),
                new TopicInterestRule(ArticleTopic.GameIndustry, 90),
                new TopicInterestRule(ArticleTopic.GameDevelopment, 95),
                new TopicInterestRule(ArticleTopic.DeveloperTools, 85),
                new TopicInterestRule(ArticleTopic.Research, 80),
                new TopicInterestRule(ArticleTopic.Business, 65),
                new TopicInterestRule(ArticleTopic.Security, 70),
                new TopicInterestRule(ArticleTopic.Other, 35)
            ],
            [
                new TopicKeywordRule(
                    ArticleTopic.ArtificialIntelligence,
                    100,
                    [
                        "ai", "artificial intelligence", "llm", "gpt",
                        "openai", "anthropic", "claude", "gemini",
                        "machine learning", "inference", "agentic",
                        "인공지능", "생성형 ai", "언어 모델"
                    ]),
                new TopicKeywordRule(
                    ArticleTopic.GameDevelopment,
                    95,
                    [
                        "unreal", "ue5", "unity", "godot", "game engine",
                        "shader", "rendering", "graphics", "게임 개발",
                        "게임 엔진", "렌더링"
                    ]),
                new TopicKeywordRule(
                    ArticleTopic.GameIndustry,
                    90,
                    [
                        "game industry", "gaming", "steam", "nexon",
                        "ncsoft", "krafton", "ubisoft", "electronic arts",
                        "게임업계", "게임 산업", "게임 출시", "게임 매출"
                    ]),
                new TopicKeywordRule(
                    ArticleTopic.DeveloperTools,
                    80,
                    [
                        "github", ".net", "c#", "compiler", "sdk", "api",
                        "database", "postgres", "docker", "kubernetes",
                        "developer tool", "visual studio", "open source",
                        "개발 도구", "오픈소스"
                    ]),
                new TopicKeywordRule(
                    ArticleTopic.Research,
                    70,
                    [
                        "paper", "arxiv", "research", "benchmark", "dataset",
                        "논문", "연구", "벤치마크", "데이터셋"
                    ]),
                new TopicKeywordRule(
                    ArticleTopic.Security,
                    60,
                    [
                        "security", "vulnerability", "cve", "exploit",
                        "breach", "보안", "취약점", "해킹"
                    ]),
                new TopicKeywordRule(
                    ArticleTopic.Business,
                    50,
                    [
                        "funding", "acquisition", "revenue", "earnings", "ipo",
                        "투자", "인수", "매출", "실적", "상장"
                    ])
            ],
            [
                new ImpactKeywordRule("release", 15),
                new ImpactKeywordRule("launch", 15),
                new ImpactKeywordRule("breaking change", 20),
                new ImpactKeywordRule("deprecated", 15),
                new ImpactKeywordRule("security", 20),
                new ImpactKeywordRule("vulnerability", 20),
                new ImpactKeywordRule("benchmark", 10),
                new ImpactKeywordRule("open source", 10),
                new ImpactKeywordRule("acquisition", 15),
                new ImpactKeywordRule("출시", 15),
                new ImpactKeywordRule("공개", 10),
                new ImpactKeywordRule("보안", 20),
                new ImpactKeywordRule("취약점", 20),
                new ImpactKeywordRule("서비스 종료", 20)
            ]);
    }

    private static void ValidateSourceTrustRules(
        IReadOnlyList<SourceTrustRule> rules)
    {
        for (int index = 0; index < rules.Count; index++)
        {
            SourceTrustRule rule = rules[index];
            ArgumentException.ThrowIfNullOrWhiteSpace(rule.Contains);
            ValidateScore(rule.Score, nameof(rule.Score));
        }
    }

    private static void ValidateTopicInterestRules(
        IReadOnlyList<TopicInterestRule> rules)
    {
        for (int index = 0; index < rules.Count; index++)
        {
            TopicInterestRule rule = rules[index];
            ValidateScore(rule.Score, nameof(rule.Score));
        }
    }

    private static void ValidateImpactRules(
        IReadOnlyList<ImpactKeywordRule> rules)
    {
        for (int index = 0; index < rules.Count; index++)
        {
            ImpactKeywordRule rule = rules[index];
            ArgumentException.ThrowIfNullOrWhiteSpace(rule.Contains);

            if (rule.Weight is < -100 or > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(rule.Weight));
            }
        }
    }

    private static void ValidateScore(int value, string parameterName)
    {
        if (value is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
