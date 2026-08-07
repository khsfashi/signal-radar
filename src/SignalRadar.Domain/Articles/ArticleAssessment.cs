namespace SignalRadar.Domain.Articles;

[Flags]
public enum ArticleTopic
{
    None = 0,
    ArtificialIntelligence = 1 << 0,
    GameIndustry = 1 << 1,
    GameDevelopment = 1 << 2,
    DeveloperTools = 1 << 3,
    Research = 1 << 4,
    Business = 1 << 5,
    Security = 1 << 6,
    Other = 1 << 7,
    Economy = 1 << 8,
    Markets = 1 << 9
}

public sealed class ArticleAssessment
{
    private const ArticleTopic SupportedTopics =
        ArticleTopic.ArtificialIntelligence
        | ArticleTopic.GameIndustry
        | ArticleTopic.GameDevelopment
        | ArticleTopic.DeveloperTools
        | ArticleTopic.Research
        | ArticleTopic.Business
        | ArticleTopic.Security
        | ArticleTopic.Other
        | ArticleTopic.Economy
        | ArticleTopic.Markets;

    public static ArticleAssessment Unclassified { get; } = new(
        ArticleTopic.Other,
        ArticleTopic.Other,
        sourceTrust: 50,
        topicInterest: 35,
        practicalImpact: 40,
        freshness: 50,
        baseScore: 43.5m,
        profileVersion: "unclassified");

    public ArticleAssessment(
        ArticleTopic topics,
        ArticleTopic primaryTopic,
        int sourceTrust,
        int topicInterest,
        int practicalImpact,
        int freshness,
        decimal baseScore,
        string profileVersion)
    {
        ValidateTopicMask(topics, nameof(topics));
        ValidatePrimaryTopic(primaryTopic, topics);
        ValidateComponent(sourceTrust, nameof(sourceTrust));
        ValidateComponent(topicInterest, nameof(topicInterest));
        ValidateComponent(practicalImpact, nameof(practicalImpact));
        ValidateComponent(freshness, nameof(freshness));

        if (baseScore is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseScore),
                "The base score must be between 0 and 100.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(profileVersion);
        string normalizedVersion = profileVersion.Trim();

        if (normalizedVersion.Length > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(profileVersion),
                "The profile version cannot exceed 100 characters.");
        }

        Topics = topics;
        PrimaryTopic = primaryTopic;
        SourceTrust = sourceTrust;
        TopicInterest = topicInterest;
        PracticalImpact = practicalImpact;
        Freshness = freshness;
        BaseScore = decimal.Round(baseScore, 2, MidpointRounding.AwayFromZero);
        ProfileVersion = normalizedVersion;
    }

    public ArticleTopic Topics { get; }

    public ArticleTopic PrimaryTopic { get; }

    public int SourceTrust { get; }

    public int TopicInterest { get; }

    public int PracticalImpact { get; }

    public int Freshness { get; }

    public decimal BaseScore { get; }

    public string ProfileVersion { get; }

    private static void ValidateTopicMask(ArticleTopic topics, string parameterName)
    {
        if (topics == ArticleTopic.None
            || (topics & ~SupportedTopics) != ArticleTopic.None)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "The topic mask contains no topic or an unsupported topic.");
        }
    }

    private static void ValidatePrimaryTopic(
        ArticleTopic primaryTopic,
        ArticleTopic topics)
    {
        int value = (int)primaryTopic;

        if (value <= 0
            || (value & (value - 1)) != 0
            || (topics & primaryTopic) == ArticleTopic.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(primaryTopic),
                "The primary topic must be one topic contained in the topic mask.");
        }
    }

    private static void ValidateComponent(int value, string parameterName)
    {
        if (value is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Score components must be between 0 and 100.");
        }
    }
}
