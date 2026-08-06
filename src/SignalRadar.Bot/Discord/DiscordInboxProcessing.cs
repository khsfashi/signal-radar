using System.Collections.Concurrent;
using SignalRadar.Application.Articles;

namespace SignalRadar.Bot.Discord;

public sealed class DiscordMessageAccessPolicy
{
    private readonly DiscordInboxOptions _options;

    public DiscordMessageAccessPolicy(DiscordInboxOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool IsAllowed(DiscordMessageEnvelope message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_options.AllowedGuildIds.Contains(message.GuildId)
            || !_options.AllowedChannelIds.Contains(message.ChannelId))
        {
            return false;
        }

        if (_options.AllowedAuthorIds.Count > 0
            && !_options.AllowedAuthorIds.Contains(message.AuthorId))
        {
            return false;
        }

        return !_options.RequireAutomatedAuthor
            || message.AuthorIsBot
            || message.AuthorIsWebhook;
    }
}

public interface IDiscordMessageReceiptStore
{
    public ValueTask<bool> TryBeginAsync(
        ulong messageId,
        CancellationToken cancellationToken);

    public ValueTask AbandonAsync(
        ulong messageId,
        CancellationToken cancellationToken);
}

public sealed class InMemoryDiscordMessageReceiptStore : IDiscordMessageReceiptStore
{
    private readonly ConcurrentDictionary<ulong, byte> _messageIds = new();

    public int Count => _messageIds.Count;

    public ValueTask<bool> TryBeginAsync(
        ulong messageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_messageIds.TryAdd(messageId, 0));
    }

    public ValueTask AbandonAsync(
        ulong messageId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _messageIds.TryRemove(messageId, out _);
        return ValueTask.CompletedTask;
    }
}

public enum DiscordInboxStatus
{
    IgnoredByPolicy = 0,
    IgnoredNoArticle = 1,
    DuplicateMessage = 2,
    ArticleAdded = 3,
    ArticleDuplicate = 4
}

public sealed record DiscordInboxResult(
    ulong MessageId,
    DiscordInboxStatus Status,
    CollectArticleResult? ArticleResult = null);

public sealed class DiscordInboxProcessor
{
    private readonly DiscordInboxOptions _options;
    private readonly DiscordMessageAccessPolicy _accessPolicy;
    private readonly DiscordMessageArticleCandidateFactory _candidateFactory;
    private readonly IDiscordMessageReceiptStore _receiptStore;
    private readonly CollectArticleUseCase _collectArticle;

    public DiscordInboxProcessor(
        DiscordInboxOptions options,
        DiscordMessageAccessPolicy accessPolicy,
        DiscordMessageArticleCandidateFactory candidateFactory,
        IDiscordMessageReceiptStore receiptStore,
        CollectArticleUseCase collectArticle)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _accessPolicy = accessPolicy ?? throw new ArgumentNullException(nameof(accessPolicy));
        _candidateFactory = candidateFactory ?? throw new ArgumentNullException(nameof(candidateFactory));
        _receiptStore = receiptStore ?? throw new ArgumentNullException(nameof(receiptStore));
        _collectArticle = collectArticle ?? throw new ArgumentNullException(nameof(collectArticle));
    }

    public async ValueTask<DiscordInboxResult> ProcessAsync(
        DiscordMessageEnvelope message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_accessPolicy.IsAllowed(message))
        {
            return new DiscordInboxResult(
                message.MessageId,
                DiscordInboxStatus.IgnoredByPolicy);
        }

        if (!_candidateFactory.TryCreate(
                message,
                _options.SourceName,
                out CollectedArticleCandidate? candidate))
        {
            return new DiscordInboxResult(
                message.MessageId,
                DiscordInboxStatus.IgnoredNoArticle);
        }

        bool claimed = await _receiptStore
            .TryBeginAsync(message.MessageId, cancellationToken)
            .ConfigureAwait(false);

        if (!claimed)
        {
            return new DiscordInboxResult(
                message.MessageId,
                DiscordInboxStatus.DuplicateMessage);
        }

        try
        {
            CollectArticleResult articleResult = await _collectArticle
                .ExecuteAsync(candidate, cancellationToken)
                .ConfigureAwait(false);

            DiscordInboxStatus status = articleResult.Status == CollectArticleStatus.Added
                ? DiscordInboxStatus.ArticleAdded
                : DiscordInboxStatus.ArticleDuplicate;

            return new DiscordInboxResult(
                message.MessageId,
                status,
                articleResult);
        }
        catch
        {
            await _receiptStore
                .AbandonAsync(message.MessageId, CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }
}
