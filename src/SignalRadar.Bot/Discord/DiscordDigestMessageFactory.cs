using System.Globalization;
using System.Text;
using Discord;
using SignalRadar.Application.Digests;

namespace SignalRadar.Bot.Discord;

public static class DiscordDigestMessageFactory
{
    public static string GetHeading(ArticleDigest digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        string periodLabel = digest.Period switch
        {
            ArticleDigestPeriod.Daily => "일간",
            ArticleDigestPeriod.Weekly => "주간",
            _ => throw new ArgumentOutOfRangeException(nameof(digest))
        };
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Signal Radar {periodLabel} 다이제스트 · {digest.Articles.Count}건");
    }

    public static Embed BuildEmbed(ArticleDigest digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        EmbedBuilder builder = new EmbedBuilder()
            .WithTitle(GetHeading(digest))
            .WithDescription(string.Create(
                CultureInfo.InvariantCulture,
                $"기간: {digest.WindowStart:yyyy-MM-dd HH:mm} ~ "
                    + $"{digest.WindowEnd:yyyy-MM-dd HH:mm} UTC\n"
                    + $"주제: {DiscordArticleInteractionCodec.GetTopicLabel(digest.Topic)}"))
            .WithTimestamp(digest.WindowEnd);

        for (int index = 0; index < digest.Articles.Count; index++)
        {
            Application.Articles.RankedArticle article = digest.Articles[index];
            string fieldName = Truncate(
                $"#{index + 1} {article.Title}",
                EmbedFieldBuilder.MaxFieldNameLength);
            StringBuilder value = new();
            value.Append("[원문 보기](")
                .Append(article.CanonicalUrl.AbsoluteUri)
                .Append(") · ")
                .Append(article.Source)
                .Append(" · 점수 ")
                .Append(article.EffectiveScore.ToString("0.00", CultureInfo.InvariantCulture))
                .Append("\n주제: ")
                .Append(DiscordArticleInteractionCodec.FormatTopics(article.Topics));
            builder.AddField(
                fieldName,
                Truncate(value.ToString(), EmbedFieldBuilder.MaxFieldValueLength),
                inline: false);
        }

        if (digest.Articles.Count == 0)
        {
            builder.AddField("새 기사 없음", "해당 기간과 주제에 맞는 기사가 없습니다.");
        }

        return builder.Build();
    }

    private static string Truncate(string value, int maximumLength)
    {
        return value.Length <= maximumLength
            ? value
            : string.Concat(value.AsSpan(0, maximumLength - 1), "…");
    }
}
