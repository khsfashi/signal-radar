using SignalRadar.Application.Articles;
using SignalRadar.Domain.Articles;
using Xunit;

namespace SignalRadar.Application.Tests.Articles;

public sealed class SavedArticleMarkdownExporterTests
{
    [Fact]
    public void Create_ProducesBoundedProviderNeutralMarkdown()
    {
        DateTimeOffset generatedAt = new(
            2026,
            8,
            6,
            10,
            30,
            0,
            TimeSpan.Zero);
        SavedArticle article = new(
            Guid.NewGuid(),
            "OpenAI [tool] update",
            new Uri("https://example.com/article"),
            "official-source",
            generatedAt.AddHours(-2),
            generatedAt.AddMinutes(-10),
            ArticleTopic.ArtificialIntelligence | ArticleTopic.DeveloperTools,
            ArticleTopic.ArtificialIntelligence,
            80m,
            3,
            95m);
        SavedArticleMarkdownExporter exporter = new();

        ArticleMarkdownExport result = exporter.Create([article], generatedAt);

        Assert.Equal(1, result.ArticleCount);
        Assert.Equal(
            "signal-radar-saved-20260806-103000Z.md",
            result.FileName);
        Assert.Contains("# Signal Radar Saved Articles", result.Content);
        Assert.Contains("OpenAI \\[tool\\] update", result.Content);
        Assert.Contains("Artificial Intelligence, Developer Tools", result.Content);
        Assert.Contains("https://example.com/article", result.Content);
        Assert.DoesNotContain("ChatGPT", result.Content);
        Assert.DoesNotContain("Gemini", result.Content);
    }

    [Fact]
    public void Create_RejectsMoreThanOneHundredArticles()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<SavedArticle> articles = new(101);

        for (int index = 0; index < 101; index++)
        {
            articles.Add(new SavedArticle(
                Guid.NewGuid(),
                $"Article {index}",
                new Uri($"https://example.com/{index}"),
                "source",
                now,
                now,
                ArticleTopic.Other,
                ArticleTopic.Other,
                50m,
                0,
                50m));
        }

        SavedArticleMarkdownExporter exporter = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => exporter.Create(
            articles,
            now));
    }
}
