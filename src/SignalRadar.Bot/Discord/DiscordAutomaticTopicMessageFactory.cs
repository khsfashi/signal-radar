using System.Globalization;
using Discord;
using SignalRadar.Application.Publishing;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Bot.Discord;

public static class DiscordAutomaticTopicMessageFactory
{
    public static string GetHeading(AutomaticTopicPublicationCandidate article)
    {
        ArgumentNullException.ThrowIfNull(article);
        return $"새 {DiscordArticleInteractionCodec.GetTopicLabel(article.PrimaryTopic)} 기사";
    }

    public static string GetForumTitle(AutomaticTopicPublicationCandidate article)
    {
        ArgumentNullException.ThrowIfNull(article);
        return Truncate(article.Title, 100);
    }

    public static Embed BuildEmbed(AutomaticTopicPublicationCandidate article)
    {
        ArgumentNullException.ThrowIfNull(article);
        string description = string.Create(
            CultureInfo.InvariantCulture,
            $"출처: {article.Source}\n"
                + $"점수: {article.EffectiveScore:0.00} "
                + $"(기본 {article.BaseScore:0.00}, 피드백 {article.FeedbackWeight:+#;-#;0})\n"
                + $"주제: {DiscordArticleInteractionCodec.FormatTopics(article.Topics)}");

        return new EmbedBuilder()
            .WithTitle(Truncate(article.Title, EmbedBuilder.MaxTitleLength))
            .WithUrl(article.CanonicalUrl.AbsoluteUri)
            .WithDescription(description)
            .WithFooter("Signal Radar 자동 토픽 공유")
            .WithTimestamp(article.PublishedAt)
            .Build();
    }

    public static MessageComponent BuildComponents(
        AutomaticTopicPublicationCandidate article)
    {
        ArgumentNullException.ThrowIfNull(article);
        ComponentBuilder builder = new();
        builder.WithButton(
            "관심",
            DiscordArticleInteractionCodec.CreateFeedbackCustomId(
                ArticleFeedbackKind.Interested,
                article.ArticleId),
            ButtonStyle.Success);
        builder.WithButton(
            "별로",
            DiscordArticleInteractionCodec.CreateFeedbackCustomId(
                ArticleFeedbackKind.NotInterested,
                article.ArticleId),
            ButtonStyle.Secondary);
        builder.WithButton(
            "저장",
            DiscordArticleInteractionCodec.CreateSaveCustomId(
                DiscordArticleSaveAction.Add,
                article.ArticleId),
            ButtonStyle.Primary);
        builder.WithButton(
            "숨김",
            DiscordArticleInteractionCodec.CreateFeedbackCustomId(
                ArticleFeedbackKind.Hidden,
                article.ArticleId),
            ButtonStyle.Danger);
        return builder.Build();
    }

    private static string Truncate(string value, int maximumLength)
    {
        return value.Length <= maximumLength
            ? value
            : string.Concat(value.AsSpan(0, maximumLength - 1), "…");
    }
}
