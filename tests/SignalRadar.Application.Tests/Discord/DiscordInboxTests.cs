using SignalRadar.Application.Articles;
using SignalRadar.Bot.Discord;
using SignalRadar.Infrastructure.Articles;
using SignalRadar.Infrastructure.Discord;
using Xunit;

namespace SignalRadar.Application.Tests.Discord;

public sealed class DiscordInboxTests
{
    [Fact]
    public void AccessPolicy_RejectsMessageFromUnlistedChannel()
    {
        DiscordInboxOptions options = CreateOptions();
        DiscordMessageAccessPolicy policy = new(options);

        Assert.False(policy.IsAllowed(CreateMessage(1, 999, "https://example.com/a")));
    }

    [Fact]
    public void CandidateFactory_PrefersEmbedTitleAndUrl()
    {
        DiscordMessageArticleCandidateFactory factory = new();
        DiscordMessageEnvelope message = new(
            1,
            10,
            20,
            30,
            AuthorIsBot: true,
            AuthorIsWebhook: false,
            string.Empty,
            [new DiscordEmbedEnvelope("AI tool release", "Release notes", "https://example.com/releases/1")],
            DateTimeOffset.UtcNow);

        bool created = factory.TryCreate(message, "geeknews", out CollectedArticleCandidate? candidate);

        Assert.True(created);
        Assert.NotNull(candidate);
        Assert.Equal("AI tool release", candidate.Title);
        Assert.Equal("https://example.com/releases/1", candidate.Url);
    }

    [Fact]
    public async Task Processor_RejectsRepeatedDiscordMessageId()
    {
        DiscordInboxProcessor processor = CreateProcessor();
        DiscordMessageEnvelope message = CreateMessage(100, 20, "https://example.com/a");

        DiscordInboxResult first = await processor.ProcessAsync(message);
        DiscordInboxResult second = await processor.ProcessAsync(message);

        Assert.Equal(DiscordInboxStatus.ArticleAdded, first.Status);
        Assert.Equal(DiscordInboxStatus.DuplicateMessage, second.Status);
    }

    [Fact]
    public async Task Processor_ReportsDuplicateArticleAcrossDifferentMessages()
    {
        DiscordInboxProcessor processor = CreateProcessor();

        DiscordInboxResult first = await processor.ProcessAsync(
            CreateMessage(100, 20, "https://example.com/a?utm_source=discord"));
        DiscordInboxResult second = await processor.ProcessAsync(
            CreateMessage(101, 20, "https://example.com/a"));

        Assert.Equal(DiscordInboxStatus.ArticleAdded, first.Status);
        Assert.Equal(DiscordInboxStatus.ArticleDuplicate, second.Status);
    }

    private static DiscordInboxProcessor CreateProcessor()
    {
        DiscordInboxOptions options = CreateOptions();
        CollectArticleUseCase collectArticle = new(
            new InMemoryArticleInbox(),
            new CanonicalUrlNormalizer(),
            TimeProvider.System);

        return new DiscordInboxProcessor(
            options,
            new DiscordMessageAccessPolicy(options),
            new DiscordMessageArticleCandidateFactory(),
            new InMemoryDiscordMessageReceiptStore(),
            collectArticle);
    }

    private static DiscordInboxOptions CreateOptions()
    {
        return new DiscordInboxOptions(
            "token",
            [10],
            [20],
            [30],
            sourceName: "geeknews");
    }

    private static DiscordMessageEnvelope CreateMessage(
        ulong messageId,
        ulong channelId,
        string url)
    {
        return new DiscordMessageEnvelope(
            messageId,
            10,
            channelId,
            30,
            AuthorIsBot: true,
            AuthorIsWebhook: false,
            $"Article {messageId}\n{url}",
            [],
            DateTimeOffset.UtcNow);
    }
}
