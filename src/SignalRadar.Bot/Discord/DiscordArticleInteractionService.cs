using System.Globalization;
using SignalRadar.Application.Articles;
using SignalRadar.Application.Summaries;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Bot.Discord;

public sealed class DiscordArticleInteractionService
{
    private readonly IArticleRankingReader _rankingReader;
    private readonly IArticleFeedbackStore _feedbackStore;
    private readonly IArticleSaveStore _saveStore;
    private readonly IArticleSavedReader _savedReader;
    private readonly SavedArticleMarkdownExporter _markdownExporter;
    private readonly TimeProvider _timeProvider;
    private readonly GenerateArticleSummaryUseCase? _summaryUseCase;

    public DiscordArticleInteractionService(
        IArticleRankingReader rankingReader,
        IArticleFeedbackStore feedbackStore,
        IArticleSaveStore saveStore,
        IArticleSavedReader savedReader,
        SavedArticleMarkdownExporter markdownExporter,
        TimeProvider timeProvider,
        GenerateArticleSummaryUseCase? summaryUseCase = null)
    {
        _rankingReader = rankingReader
            ?? throw new ArgumentNullException(nameof(rankingReader));
        _feedbackStore = feedbackStore
            ?? throw new ArgumentNullException(nameof(feedbackStore));
        _saveStore = saveStore
            ?? throw new ArgumentNullException(nameof(saveStore));
        _savedReader = savedReader
            ?? throw new ArgumentNullException(nameof(savedReader));
        _markdownExporter = markdownExporter
            ?? throw new ArgumentNullException(nameof(markdownExporter));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        _summaryUseCase = summaryUseCase;
    }

    public bool SummaryEnabled => _summaryUseCase is not null;

    public ValueTask<IReadOnlyList<RankedArticle>> GetTopAsync(
        ulong userId,
        int days,
        ArticleTopic topic,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        ValidateWindow(days, limit, maximumDays: 30, maximumLimit: 5);
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
        ValidateWindow(days, limit, maximumDays: 30, maximumLimit: 5);
        ArticleSearchQuery query = new(
            text,
            _timeProvider.GetUtcNow().AddDays(-days),
            topic,
            limit,
            DiscordArticleInteractionCodec.CreateActorId(userId));

        return _rankingReader.SearchAsync(query, cancellationToken);
    }

    public ValueTask<IReadOnlyList<SavedArticle>> GetSavedAsync(
        ulong userId,
        int days,
        ArticleTopic topic,
        int limit,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        ValidateWindow(days, limit, maximumDays: 3650, maximumLimit: 100);
        SavedArticleQuery query = new(
            _timeProvider.GetUtcNow().AddDays(-days),
            topic,
            limit,
            DiscordArticleInteractionCodec.CreateActorId(userId));
        return _savedReader.GetSavedAsync(query, cancellationToken);
    }

    public async ValueTask<ArticleMarkdownExport> ExportSavedAsync(
        ulong userId,
        int days,
        ArticleTopic topic,
        int limit,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<SavedArticle> articles = await GetSavedAsync(
            userId,
            days,
            topic,
            limit,
            cancellationToken).ConfigureAwait(false);
        return _markdownExporter.Create(articles, _timeProvider.GetUtcNow());
    }

    public async ValueTask<GeneratedArticleSummary?> SummarizeSavedAsync(
        ulong userId,
        int days,
        ArticleTopic topic,
        int limit,
        string language,
        CancellationToken cancellationToken)
    {
        GenerateArticleSummaryUseCase useCase = _summaryUseCase
            ?? throw new InvalidOperationException(
                "Article summary generation is not configured.");
        ValidateUserId(userId);
        ValidateWindow(days, limit, maximumDays: 3650, maximumLimit: 20);
        IReadOnlyList<SavedArticle> articles = await GetSavedAsync(
            userId,
            days,
            topic,
            limit,
            cancellationToken).ConfigureAwait(false);

        if (articles.Count == 0)
        {
            return null;
        }

        return await useCase.GenerateAsync(
            articles,
            language,
            cancellationToken).ConfigureAwait(false);
    }

    public ValueTask SetFeedbackAsync(
        ulong userId,
        Guid articleId,
        ArticleFeedbackKind kind,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        ValidateArticleId(articleId);
        _ = ArticleFeedbackWeights.GetWeight(kind);
        return _feedbackStore.SetAsync(
            articleId,
            DiscordArticleInteractionCodec.CreateActorId(userId),
            kind,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public ValueTask<bool> SaveAsync(
        ulong userId,
        Guid articleId,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        ValidateArticleId(articleId);
        return _saveStore.TryAddAsync(
            articleId,
            DiscordArticleInteractionCodec.CreateActorId(userId),
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }

    public ValueTask<bool> RemoveSavedAsync(
        ulong userId,
        Guid articleId,
        CancellationToken cancellationToken)
    {
        ValidateUserId(userId);
        ValidateArticleId(articleId);
        return _saveStore.RemoveAsync(
            articleId,
            DiscordArticleInteractionCodec.CreateActorId(userId),
            cancellationToken);
    }

    private static void ValidateUserId(ulong userId)
    {
        if (userId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId));
        }
    }

    private static void ValidateArticleId(Guid articleId)
    {
        if (articleId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(articleId));
        }
    }

    private static void ValidateWindow(
        int days,
        int limit,
        int maximumDays,
        int maximumLimit)
    {
        if (days < 1 || days > maximumDays)
        {
            throw new ArgumentOutOfRangeException(nameof(days));
        }

        if (limit < 1 || limit > maximumLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }
    }
}

public enum DiscordArticleSaveAction
{
    Add,
    Remove
}

public static class DiscordArticleInteractionCodec
{
    private const string FeedbackPrefix = "sr:feedback:";
    private const string SavePrefix = "sr:save:";

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
        ValidateArticleId(articleId);
        string action = kind switch
        {
            ArticleFeedbackKind.Interested => "interested",
            ArticleFeedbackKind.NotInterested => "not-interested",
            ArticleFeedbackKind.Hidden => "hidden",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{FeedbackPrefix}{action}:{articleId:N}");
    }

    public static string CreateSaveCustomId(
        DiscordArticleSaveAction action,
        Guid articleId)
    {
        ValidateArticleId(articleId);
        string actionName = action switch
        {
            DiscordArticleSaveAction.Add => "add",
            DiscordArticleSaveAction.Remove => "remove",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{SavePrefix}{actionName}:{articleId:N}");
    }

    public static bool TryParseFeedbackCustomId(
        string? customId,
        out Guid articleId,
        out ArticleFeedbackKind kind)
    {
        articleId = Guid.Empty;
        kind = default;

        if (!TryReadPayload(customId, FeedbackPrefix, out ReadOnlySpan<char> action, out ReadOnlySpan<char> identifier)
            || !TryParseKind(action, out kind)
            || !TryParseArticleId(identifier, out articleId))
        {
            articleId = Guid.Empty;
            kind = default;
            return false;
        }

        return true;
    }

    public static bool TryParseSaveCustomId(
        string? customId,
        out Guid articleId,
        out DiscordArticleSaveAction action)
    {
        articleId = Guid.Empty;
        action = default;

        if (!TryReadPayload(customId, SavePrefix, out ReadOnlySpan<char> actionSpan, out ReadOnlySpan<char> identifier)
            || !TryParseSaveAction(actionSpan, out action)
            || !TryParseArticleId(identifier, out articleId))
        {
            articleId = Guid.Empty;
            action = default;
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
            "economy" or "경제" => ArticleTopic.Economy,
            "markets" or "stocks" or "stock" or "주식" => ArticleTopic.Markets,
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
            ArticleTopic.Economy => "경제",
            ArticleTopic.Markets => "증시·주식",
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

        List<string> labels = new(10);
        AddTopicLabel(labels, topics, ArticleTopic.ArtificialIntelligence);
        AddTopicLabel(labels, topics, ArticleTopic.GameIndustry);
        AddTopicLabel(labels, topics, ArticleTopic.GameDevelopment);
        AddTopicLabel(labels, topics, ArticleTopic.DeveloperTools);
        AddTopicLabel(labels, topics, ArticleTopic.Research);
        AddTopicLabel(labels, topics, ArticleTopic.Business);
        AddTopicLabel(labels, topics, ArticleTopic.Security);
        AddTopicLabel(labels, topics, ArticleTopic.Economy);
        AddTopicLabel(labels, topics, ArticleTopic.Markets);
        AddTopicLabel(labels, topics, ArticleTopic.Other);
        return string.Join(", ", labels);
    }

    private static bool TryReadPayload(
        string? customId,
        string prefix,
        out ReadOnlySpan<char> action,
        out ReadOnlySpan<char> identifier)
    {
        action = default;
        identifier = default;

        if (string.IsNullOrWhiteSpace(customId)
            || !customId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> payload = customId.AsSpan(prefix.Length);
        int separatorIndex = payload.IndexOf(':');

        if (separatorIndex <= 0 || separatorIndex == payload.Length - 1)
        {
            return false;
        }

        action = payload[..separatorIndex];
        identifier = payload[(separatorIndex + 1)..];
        return true;
    }

    private static bool TryParseArticleId(
        ReadOnlySpan<char> identifier,
        out Guid articleId)
    {
        return Guid.TryParseExact(identifier, "N", out articleId)
            && articleId != Guid.Empty;
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

    private static bool TryParseSaveAction(
        ReadOnlySpan<char> action,
        out DiscordArticleSaveAction saveAction)
    {
        if (action.SequenceEqual("add"))
        {
            saveAction = DiscordArticleSaveAction.Add;
            return true;
        }

        if (action.SequenceEqual("remove"))
        {
            saveAction = DiscordArticleSaveAction.Remove;
            return true;
        }

        saveAction = default;
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

    private static void ValidateArticleId(Guid articleId)
    {
        if (articleId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(articleId));
        }
    }
}
