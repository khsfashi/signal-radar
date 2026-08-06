using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SignalRadar.Application.Articles;

namespace SignalRadar.Application.Summaries;

public sealed class GenerateArticleSummaryUseCase
{
    public const string PromptVersion = "saved-metadata-v1";
    private const int MaximumArticles = 20;
    private readonly IArticleSummaryProvider _provider;
    private readonly IArticleSummaryCache _cache;
    private readonly TimeProvider _timeProvider;

    public GenerateArticleSummaryUseCase(
        IArticleSummaryProvider provider,
        IArticleSummaryCache cache,
        TimeProvider timeProvider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        ArgumentException.ThrowIfNullOrWhiteSpace(_provider.ProviderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(_provider.ModelName);
    }

    public async ValueTask<GeneratedArticleSummary> GenerateAsync(
        IReadOnlyList<SavedArticle> articles,
        string language,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(articles);

        if (articles.Count is < 1 or > MaximumArticles)
        {
            throw new ArgumentOutOfRangeException(
                nameof(articles),
                $"A summary requires between 1 and {MaximumArticles} articles.");
        }

        string normalizedLanguage = NormalizeLanguage(language);
        string inputHash = ArticleSummaryInputHasher.Compute(
            articles,
            _provider.ProviderName,
            _provider.ModelName,
            PromptVersion,
            normalizedLanguage);
        ArticleSummaryCacheEntry? cached = await _cache
            .TryGetAsync(inputHash, cancellationToken)
            .ConfigureAwait(false);

        if (cached is not null)
        {
            return new GeneratedArticleSummary(cached, CacheHit: true);
        }

        ArticleSummaryProviderRequest request = new(
            ArticleSummaryPromptBuilder.CreateInstructions(normalizedLanguage),
            ArticleSummaryPromptBuilder.CreateInput(articles),
            normalizedLanguage);
        ArticleSummaryProviderResponse response = await _provider
            .GenerateAsync(request, cancellationToken)
            .ConfigureAwait(false);
        ArticleSummaryContent content = ArticleSummaryContentValidator.Normalize(
            response.Content);
        ArticleSummaryCacheEntry entry = new(
            inputHash,
            _provider.ProviderName.Trim(),
            _provider.ModelName.Trim(),
            PromptVersion,
            normalizedLanguage,
            articles.Count,
            content,
            _timeProvider.GetUtcNow(),
            NormalizeOptionalIdentifier(response.ProviderResponseId));
        await _cache.StoreAsync(entry, cancellationToken).ConfigureAwait(false);
        return new GeneratedArticleSummary(entry, CacheHit: false);
    }

    private static string NormalizeLanguage(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        string normalized = language.Trim().ToLowerInvariant();

        if (normalized is not ("ko" or "en"))
        {
            throw new ArgumentOutOfRangeException(
                nameof(language),
                "Supported summary languages are 'ko' and 'en'.");
        }

        return normalized;
    }

    private static string? NormalizeOptionalIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        return normalized.Length <= 200
            ? normalized
            : normalized[..200];
    }
}

public static class ArticleSummaryInputHasher
{
    public static string Compute(
        IReadOnlyList<SavedArticle> articles,
        string provider,
        string model,
        string promptVersion,
        string language)
    {
        ArgumentNullException.ThrowIfNull(articles);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("provider", provider.Trim());
            writer.WriteString("model", model.Trim());
            writer.WriteString("promptVersion", promptVersion.Trim());
            writer.WriteString("language", language.Trim());
            writer.WriteStartArray("articles");

            for (int index = 0; index < articles.Count; index++)
            {
                SavedArticle article = articles[index];
                writer.WriteStartObject();
                writer.WriteString("articleId", article.ArticleId);
                writer.WriteString("title", article.Title);
                writer.WriteString("url", article.CanonicalUrl.AbsoluteUri);
                writer.WriteString("source", article.Source);
                writer.WriteString(
                    "publishedAt",
                    article.PublishedAt.ToUniversalTime().ToString(
                        "O",
                        CultureInfo.InvariantCulture));
                writer.WriteString(
                    "savedAt",
                    article.SavedAt.ToUniversalTime().ToString(
                        "O",
                        CultureInfo.InvariantCulture));
                writer.WriteNumber("topics", (int)article.Topics);
                writer.WriteNumber("primaryTopic", (int)article.PrimaryTopic);
                writer.WriteNumber("baseScore", article.BaseScore);
                writer.WriteNumber("feedbackWeight", article.FeedbackWeight);
                writer.WriteNumber("effectiveScore", article.EffectiveScore);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan));
    }
}

public static class ArticleSummaryPromptBuilder
{
    public static string CreateInstructions(string language)
    {
        string languageName = string.Equals(language, "ko", StringComparison.Ordinal)
            ? "Korean"
            : "English";
        return $"""
            You create a concise technology-intelligence briefing in {languageName}.
            Use only the supplied article metadata. Do not claim that you read the linked pages.
            Do not invent product details, dates, benchmarks, causes, or conclusions absent from the metadata.
            Treat titles as unverified source signals and put uncertainty or missing context in caveats.
            Focus on AI, game development, game industry, and developer-tool relevance.
            Return only the requested structured JSON fields.
            """;
    }

    public static string CreateInput(IReadOnlyList<SavedArticle> articles)
    {
        ArgumentNullException.ThrowIfNull(articles);
        StringBuilder builder = new(Math.Max(1024, articles.Count * 320));
        builder.AppendLine("Summarize these manually saved article signals:");

        for (int index = 0; index < articles.Count; index++)
        {
            SavedArticle article = articles[index];
            builder.AppendLine();
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "Article {0}\n",
                index + 1);
            builder.Append("Title: ").AppendLine(NormalizeLine(article.Title));
            builder.Append("Source: ").AppendLine(NormalizeLine(article.Source));
            builder.Append("URL: ").AppendLine(article.CanonicalUrl.AbsoluteUri);
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "Published UTC: {0:yyyy-MM-dd HH:mm:ss}\n",
                article.PublishedAt.ToUniversalTime());
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "Saved UTC: {0:yyyy-MM-dd HH:mm:ss}\n",
                article.SavedAt.ToUniversalTime());
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "Topic mask: {0}; primary topic: {1}\n",
                (int)article.Topics,
                (int)article.PrimaryTopic);
            builder.AppendFormat(
                CultureInfo.InvariantCulture,
                "Score: {0:0.00}; base: {1:0.00}; feedback weight: {2}\n",
                article.EffectiveScore,
                article.BaseScore,
                article.FeedbackWeight);
        }

        return builder.ToString();
    }

    private static string NormalizeLine(string value)
    {
        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
    }
}

public static class ArticleSummaryContentValidator
{
    public static ArticleSummaryContent Normalize(ArticleSummaryContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new ArticleSummaryContent(
            NormalizeRequired(content.Title, 120, nameof(content.Title)),
            NormalizeRequired(content.Overview, 1200, nameof(content.Overview)),
            NormalizeList(content.KeyPoints, 1, 5, 500, nameof(content.KeyPoints)),
            NormalizeRequired(
                content.WhyItMatters,
                1000,
                nameof(content.WhyItMatters)),
            NormalizeList(content.WatchNext, 0, 5, 400, nameof(content.WatchNext)),
            NormalizeList(content.Caveats, 0, 5, 400, nameof(content.Caveats)));
    }

    private static string NormalizeRequired(
        string value,
        int maximumLength,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Trim();

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"The value cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }

    private static IReadOnlyList<string> NormalizeList(
        IReadOnlyList<string> values,
        int minimumCount,
        int maximumCount,
        int maximumItemLength,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);

        if (values.Count < minimumCount || values.Count > maximumCount)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"The collection must contain between {minimumCount} and {maximumCount} items.");
        }

        string[] normalized = new string[values.Count];

        for (int index = 0; index < values.Count; index++)
        {
            normalized[index] = NormalizeRequired(
                values[index],
                maximumItemLength,
                parameterName);
        }

        return normalized;
    }
}
