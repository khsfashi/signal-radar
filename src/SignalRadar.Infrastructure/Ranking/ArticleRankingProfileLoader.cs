using System.Text.Json;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Infrastructure.Ranking;

public sealed class ArticleRankingProfileLoader
{
    private const long MaximumProfileBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async ValueTask<ArticleRankingProfile> LoadAsync(
        string? path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ArticleRankingProfile.CreateDefault();
        }

        string fullPath = Path.GetFullPath(path.Trim());
        FileInfo fileInfo = new(fullPath);

        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException(
                "The article ranking profile was not found.",
                fullPath);
        }

        if (fileInfo.Length is <= 0 or > MaximumProfileBytes)
        {
            throw new InvalidDataException(
                "The article ranking profile must be between 1 byte and 1 MiB.");
        }

        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            useAsync: true);
        ProfileDocument document = await JsonSerializer
            .DeserializeAsync<ProfileDocument>(
                stream,
                SerializerOptions,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException(
                "The article ranking profile is empty.");

        return CreateProfile(document);
    }

    private static ArticleRankingProfile CreateProfile(ProfileDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(document.Version);
        SourceTrustDocument[] sourceTrustDocuments = document.SourceTrustRules
            ?? throw new InvalidDataException(
                "The ranking profile requires sourceTrustRules.");
        TopicInterestDocument[] topicInterestDocuments = document.TopicInterestRules
            ?? throw new InvalidDataException(
                "The ranking profile requires topicInterestRules.");
        TopicKeywordDocument[] topicDocuments = document.TopicRules
            ?? throw new InvalidDataException(
                "The ranking profile requires topicRules.");
        ImpactKeywordDocument[] impactDocuments = document.ImpactRules
            ?? throw new InvalidDataException(
                "The ranking profile requires impactRules.");

        SourceTrustRule[] sourceTrustRules =
            new SourceTrustRule[sourceTrustDocuments.Length];

        for (int index = 0; index < sourceTrustDocuments.Length; index++)
        {
            SourceTrustDocument rule = sourceTrustDocuments[index];
            sourceTrustRules[index] = new SourceTrustRule(
                RequireText(rule.Contains, "sourceTrustRules.contains"),
                rule.Score);
        }

        TopicInterestRule[] topicInterestRules =
            new TopicInterestRule[topicInterestDocuments.Length];

        for (int index = 0; index < topicInterestDocuments.Length; index++)
        {
            TopicInterestDocument rule = topicInterestDocuments[index];
            topicInterestRules[index] = new TopicInterestRule(
                ParseTopic(rule.Topic, "topicInterestRules.topic"),
                rule.Score);
        }

        TopicKeywordRule[] topicRules = new TopicKeywordRule[topicDocuments.Length];

        for (int index = 0; index < topicDocuments.Length; index++)
        {
            TopicKeywordDocument rule = topicDocuments[index];
            string[] keywords = rule.Keywords
                ?? throw new InvalidDataException(
                    "Every topic rule requires keywords.");
            topicRules[index] = new TopicKeywordRule(
                ParseTopic(rule.Topic, "topicRules.topic"),
                rule.Priority,
                keywords);
        }

        ImpactKeywordRule[] impactRules =
            new ImpactKeywordRule[impactDocuments.Length];

        for (int index = 0; index < impactDocuments.Length; index++)
        {
            ImpactKeywordDocument rule = impactDocuments[index];
            impactRules[index] = new ImpactKeywordRule(
                RequireText(rule.Contains, "impactRules.contains"),
                rule.Weight);
        }

        return new ArticleRankingProfile(
            document.Version,
            document.DefaultSourceTrust,
            document.DefaultTopicInterest,
            document.BasePracticalImpact,
            sourceTrustRules,
            topicInterestRules,
            topicRules,
            impactRules);
    }

    private static ArticleTopic ParseTopic(string? text, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(text)
            || !Enum.TryParse(
                text.Trim(),
                ignoreCase: true,
                out ArticleTopic topic)
            || topic == ArticleTopic.None)
        {
            throw new InvalidDataException(
                $"Ranking profile field '{fieldName}' contains an invalid topic.");
        }

        int value = (int)topic;

        if ((value & (value - 1)) != 0)
        {
            throw new InvalidDataException(
                $"Ranking profile field '{fieldName}' must contain one topic.");
        }

        return topic;
    }

    private static string RequireText(string? text, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException(
                $"Ranking profile field '{fieldName}' is required.");
        }

        return text.Trim();
    }

    private sealed class ProfileDocument
    {
        public string? Version { get; init; }

        public int DefaultSourceTrust { get; init; }

        public int DefaultTopicInterest { get; init; }

        public int BasePracticalImpact { get; init; }

        public SourceTrustDocument[]? SourceTrustRules { get; init; }

        public TopicInterestDocument[]? TopicInterestRules { get; init; }

        public TopicKeywordDocument[]? TopicRules { get; init; }

        public ImpactKeywordDocument[]? ImpactRules { get; init; }
    }

    private sealed class SourceTrustDocument
    {
        public string? Contains { get; init; }

        public int Score { get; init; }
    }

    private sealed class TopicInterestDocument
    {
        public string? Topic { get; init; }

        public int Score { get; init; }
    }

    private sealed class TopicKeywordDocument
    {
        public string? Topic { get; init; }

        public int Priority { get; init; }

        public string[]? Keywords { get; init; }
    }

    private sealed class ImpactKeywordDocument
    {
        public string? Contains { get; init; }

        public int Weight { get; init; }
    }
}
