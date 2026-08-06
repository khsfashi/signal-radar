using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using SignalRadar.Application.Articles;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Bot.Discord;

public sealed class DiscordArticleInteractionService
{
    private readonly IArticleRankingReader _rankingReader;
    private readonly IArticleFeedbackStore _feedbackStore;
    private readonly TimeProvider _timeProvider;

    public DiscordArticleInteractionService(
        IArticleRankingReader rankingReader,
        IArticleFeedbackStore feedbackStore,
        TimeProvider timeProvider)
    {
        _rankingReader = rankingReader
            ?? throw new ArgumentNullException(nameof(rankingReader));
        _feedbackStore = feedbackStore
            ?? throw new ArgumentNullException(nameof(feedbackStore));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public ValueTask<IReadOnlyList<RankedArticle>> GetTopAsync(
        ulong userId,
        int days,
        ArticleTopic topic,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        ValidateWindow(days, limit);
        DateTimeOffset since = _timeProvider.GetUtcNow().AddDays(-days);
        ArticleRankingQuery query = new(
            since,
            topic,
            limit,
            DiscordArticleInteractionCodec.CreateActorId(userId));

        return _rankingReader.GetTopAsync(query, cancellationToken);
    }

    public ValueTask<IReadOnlyList<RankedArticle>> SearchAsync(
        ulong userId,
        string text,
        int days,
        ArticleTopic topic,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        ValidateWindow(days, limit);
        ArticleSearchQuery query = new(
            text,
            _timeProvider.GetUtcNow().AddDays(-days),
            topic,
            limit,
            DiscordArticleInteractionCodec.CreateActorId(userId));

        return _rankingReader.SearchAsync(query, cancellationToken);
    }

    public ValueTask SetFeedbackAsync(
        ulong userId,
        Guid articleId,
        ArticleFeedbackKind kind,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);

        if (articleId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(articleId));
        }

        _ = ArticleFeedbackWeights.GetWeight(kind);
        return _feedbackStore.SetAsync(
            articleId,
            DiscordArticleInteractionCodec.CreateActorId(userId),
            kind,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private static void ValidateUserId(ulong userId)
    {
        if (userId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }
    }

    private static void ValidateWindow(int days, int limit)
    {
        if (days is < 1 or > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(days));
        }

        if (limit is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }
    }
}

public static class DiscordArticleInteractionCodec
{
    private const string Prefix = "sr:feedback:";

    public static string CreateActorId(ulong userId)
    {
        if (userId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"discord:{userId}");
    }

    public static string CreateFeedbackCustomId(
        ArticleFeedbackKind kind,
        Guid articleId)
    {
        if (articleId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(articleId));
        }

        string action = kind switch
        {
            ArticleFeedbackKind.Interested => "interested",
            ArticleFeedbackKind.NotInterested => "not-interested",
            ArticleFeedbackKind.Hidden => "hidden",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix}{action}:{articleId:N}");
    }

    public static bool TryParseFeedbackCustomId(
        string? customId,
        out Guid articleId,
        out ArticleFeedbackKind kind)
    {
        articleId = Guid.Empty;
        kind = default;

        if (string.IsNullOrWhiteSpace(customId)
            || !customId.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> payload = customId.AsSpan(Prefix.Length);
        int separatorIndex = payload.IndexOf(':');

        if (separatorIndex <= 0 || separatorIndex == payload.Length - 1)
        {
            return false;
        }

        ReadOnlySpan<char> action = payload[..separatorIndex];
        ReadOnlySpan<char> identifier = payload[(separatorIndex + 1)..];

        if (!TryParseKind(action, out kind)
            || !Guid.TryParseExact(identifier, "N", out articleId)
            || articleId == Guid.Empty)
        {
            articleId = Guid.Empty;
            kind = default;
            return false;
        }

        return true;
    }

    public static ArticleTopic ParseTopic(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" => ArticleTopic.None,
            "ai" => ArticleTopic.ArtificialIntelligence,
            "game-industry" => ArticleTopic.GameIndustry,
            "game-development" => ArticleTopic.GameDevelopment,
            "developer-tools" => ArticleTopic.DeveloperTools,
            "research" => ArticleTopic.Research,
            "business" => ArticleTopic.Business,
            "security" => ArticleTopic.Security,
            "other" => ArticleTopic.Other,
            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                "The Discord topic value is not supported.")
        };
    }

    public static string GetTopicLabel(ArticleTopic topic)
    {
        return topic switch
        {
            ArticleTopic.None => "전체",
            ArticleTopic.ArtificialIntelligence => "AI",
            ArticleTopic.GameIndustry => "게임 산업",
            ArticleTopic.GameDevelopment => "게임 개발",
            ArticleTopic.DeveloperTools => "개발 도구",
            ArticleTopic.Research => "연구",
            ArticleTopic.Business => "비즈니스",
            ArticleTopic.Security => "보안",
            ArticleTopic.Other => "기타",
            _ => "복합 주제"
        };
    }

    public static string FormatTopics(ArticleTopic topics)
    {
        if (topics == ArticleTopic.None)
        {
            return "없음";
        }

        List<string> labels = new(8);
        AddTopicLabel(labels, topics, ArticleTopic.ArtificialIntelligence);
        AddTopicLabel(labels, topics, ArticleTopic.GameIndustry);
        AddTopicLabel(labels, topics, ArticleTopic.GameDevelopment);
        AddTopicLabel(labels, topics, ArticleTopic.DeveloperTools);
        AddTopicLabel(labels, topics, ArticleTopic.Research);
        AddTopicLabel(labels, topics, ArticleTopic.Business);
        AddTopicLabel(labels, topics, ArticleTopic.Security);
        AddTopicLabel(labels, topics, ArticleTopic.Other);
        return string.Join(", ", labels);
    }

    private static bool TryParseKind(
        ReadOnlySpan<char> action,
        out ArticleFeedbackKind kind)
    {
        if (action.SequenceEqual("interested"))
        {
            kind = ArticleFeedbackKind.Interested;
            return true;
        }

        if (action.SequenceEqual("not-interested"))
        {
            kind = ArticleFeedbackKind.NotInterested;
            return true;
        }

        if (action.SequenceEqual("hidden"))
        {
            kind = ArticleFeedbackKind.Hidden;
            return true;
        }

        kind = default;
        return false;
    }

    private static void AddTopicLabel(
        List<string> labels,
        ArticleTopic topics,
        ArticleTopic topic)
    {
        if ((topics & topic) != ArticleTopic.None)
        {
            labels.Add(GetTopicLabel(topic));
        }
    }
}
