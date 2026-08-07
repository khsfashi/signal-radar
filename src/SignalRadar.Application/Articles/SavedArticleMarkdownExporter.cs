using System.Globalization;
using System.Text;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Application.Articles;

public sealed record ArticleMarkdownExport(
    string FileName,
    string Content,
    int ArticleCount);

public sealed class SavedArticleMarkdownExporter
{
    public ArticleMarkdownExport Create(
        IReadOnlyList<SavedArticle> articles,
        DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(articles);

        if (articles.Count > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(articles),
                "A Markdown export cannot contain more than 100 articles.");
        }

        DateTimeOffset generatedUtc = generatedAt.ToUniversalTime();
        StringBuilder builder = new(capacity: Math.Max(1024, articles.Count * 512));
        builder.AppendLine("# Signal Radar Saved Articles");
        builder.AppendLine();
        builder.AppendFormat(
            CultureInfo.InvariantCulture,
            "- Generated: {0:yyyy-MM-dd HH:mm:ss} UTC\n",
            generatedUtc);
        builder.AppendFormat(
            CultureInfo.InvariantCulture,
            "- Article count: {0}\n",
            articles.Count);
        builder.AppendLine();
        builder.AppendLine(
            "> These are manually saved source links. Verify important claims against the linked originals before acting on them.");

        for (int index = 0; index < articles.Count; index++)
        {
            SavedArticle article = articles[index];
            builder.AppendLine();
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "## {0}. [{1}]({2})\n",
                index + 1,
                EscapeLinkText(article.Title),
                article.CanonicalUrl.AbsoluteUri);
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "- Source: {0}\n",
                NormalizeInlineText(article.Source));
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "- Published: {0:yyyy-MM-dd HH:mm:ss} UTC\n",
                article.PublishedAt.ToUniversalTime());
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "- Saved: {0:yyyy-MM-dd HH:mm:ss} UTC\n",
                article.SavedAt.ToUniversalTime());
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "- Topics: {0}\n",
                FormatTopics(article.Topics));
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "- Score: {0:0.00} (base {1:0.00}, feedback {2:+#;-#;0})\n",
                article.EffectiveScore,
                article.BaseScore,
                article.FeedbackWeight);
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "- URL: <{0}>\n",
                article.CanonicalUrl.AbsoluteUri);
        }

        string fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"signal-radar-saved-{generatedUtc:yyyyMMdd-HHmmss}Z.md");
        return new ArticleMarkdownExport(
            fileName,
            builder.ToString(),
            articles.Count);
    }

    private static string EscapeLinkText(string value)
    {
        return NormalizeInlineText(value)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
    }

    private static string NormalizeInlineText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }

    private static string FormatTopics(ArticleTopic topics)
    {
        List<string> names = new(8);
        AddTopic(names, topics, ArticleTopic.ArtificialIntelligence, "Artificial Intelligence");
        AddTopic(names, topics, ArticleTopic.GameIndustry, "Game Industry");
        AddTopic(names, topics, ArticleTopic.GameDevelopment, "Game Development");
        AddTopic(names, topics, ArticleTopic.DeveloperTools, "Developer Tools");
        AddTopic(names, topics, ArticleTopic.Research, "Research");
        AddTopic(names, topics, ArticleTopic.Business, "Business");
        AddTopic(names, topics, ArticleTopic.Security, "Security");
        AddTopic(names, topics, ArticleTopic.Other, "Other");
        return names.Count > 0 ? string.Join(", ", names) : "None";
    }

    private static void AddTopic(
        List<string> names,
        ArticleTopic topics,
        ArticleTopic topic,
        string name)
    {
        if ((topics & topic) != ArticleTopic.None)
        {
            names.Add(name);
        }
    }
}
