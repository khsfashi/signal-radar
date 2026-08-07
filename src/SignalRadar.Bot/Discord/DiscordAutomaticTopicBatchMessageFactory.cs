using System.Globalization;
using System.Text;
using Discord;
using SignalRadar.Application.Publishing;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Bot.Discord;

public static class DiscordAutomaticTopicBatchMessageFactory
{
    public static string GetHeading(
        ArticleTopic topic,
        int articleCount,
        TimeSpan batchWindow)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"📰 {DiscordArticleInteractionCodec.GetTopicLabel(topic)} 뉴스 · 최근 {batchWindow.TotalMinutes:0}분 · {articleCount}건");
    }

    public static string GetForumTitle(
        ArticleTopic topic,
        DateTimeOffset createdAt)
    {
        string title = string.Create(
            CultureInfo.InvariantCulture,
            $"{DiscordArticleInteractionCodec.GetTopicLabel(topic)} 뉴스 · {createdAt:MM-dd HH:mm}");
        return title.Length <= 100 ? title : title[..100];
    }

    public static Embed BuildEmbed(
        ArticleTopic topic,
        IReadOnlyList<AutomaticTopicPublicationCandidate> articles,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(articles);

        if (articles.Count == 0)
        {
            throw new ArgumentException(
                "At least one article is required for a topic batch.",
                nameof(articles));
        }

        StringBuilder description = new(capacity: Math.Min(4096, articles.Count * 240));

        for (int index = 0; index < articles.Count; index++)
        {
            AutomaticTopicPublicationCandidate article = articles[index];
            string title = EscapeMarkdown(article.Title);
            string line = string.Create(
                CultureInfo.InvariantCulture,
                $"**{index + 1}. [{title}]({article.CanonicalUrl.AbsoluteUri})**\n"
                    + $"{article.Source} · 점수 {article.EffectiveScore:0.#}\n");

            if (description.Length + line.Length > 4000)
            {
                description.Append("\n…나머지 기사는 다음 배치에서 표시됩니다.");
                break;
            }

            if (index > 0)
            {
                description.Append('\n');
            }

            description.Append(line);
        }

        return new EmbedBuilder()
            .WithTitle($"{DiscordArticleInteractionCodec.GetTopicLabel(topic)} 브리핑")
            .WithDescription(description.ToString())
            .WithFooter("Signal Radar · 제목 자동 번역 · 원문 링크 유지")
            .WithTimestamp(createdAt)
            .Build();
    }

    private static string EscapeMarkdown(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
    }
}
