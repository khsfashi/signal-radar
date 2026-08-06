using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace SignalRadar.Infrastructure.Summaries;

internal sealed record HtmlArticleText(string Text, string? DocumentTitle);

internal sealed class HtmlArticleTextExtractor
{
    private const int MaximumCandidates = 1000;
    private static readonly string[] BoilerplateTokens =
    [
        "advert",
        "banner",
        "breadcrumb",
        "comment",
        "consent",
        "cookie",
        "footer",
        "menu",
        "modal",
        "nav",
        "newsletter",
        "popup",
        "promo",
        "related",
        "share",
        "sidebar",
        "social",
        "subscribe"
    ];
    private static readonly string[] BoilerplateLineFragments =
    [
        "accept all cookies",
        "all rights reserved",
        "cookie policy",
        "privacy policy",
        "sign up for",
        "subscribe to",
        "쿠키를 사용",
        "개인정보 처리방침",
        "뉴스레터 구독"
    ];
    private readonly int _minimumCharacters;
    private readonly int _maximumCharacters;

    public HtmlArticleTextExtractor(
        int minimumCharacters,
        int maximumCharacters)
    {
        if (minimumCharacters < 100)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumCharacters));
        }

        if (maximumCharacters <= minimumCharacters)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        }

        _minimumCharacters = minimumCharacters;
        _maximumCharacters = maximumCharacters;
    }

    public async ValueTask<HtmlArticleText?> ExtractAsync(
        string html,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(html);
        HtmlParser parser = new();
        IHtmlDocument document = await parser
            .ParseDocumentAsync(html, cancellationToken)
            .ConfigureAwait(false);
        RemoveNonContentElements(document);
        IElement? candidate = SelectBestCandidate(document);

        if (candidate is null)
        {
            return null;
        }

        string text = BuildStructuredText(candidate);

        if (text.Length < _minimumCharacters)
        {
            return null;
        }

        return new HtmlArticleText(
            text,
            NormalizeOptionalLine(document.Title));
    }

    private static void RemoveNonContentElements(IParentNode document)
    {
        IHtmlCollection<IElement> unconditional = document.QuerySelectorAll(
            "script,style,noscript,svg,canvas,form,nav,footer,aside,header,iframe,template,[hidden],[aria-hidden='true']");
        RemoveElements(unconditional);

        IHtmlCollection<IElement> possibleBoilerplate = document.QuerySelectorAll(
            "div,section,ul,ol,p");
        List<IElement> removals = [];

        for (int index = 0; index < possibleBoilerplate.Length; index++)
        {
            IElement element = possibleBoilerplate[index];

            if (HasBoilerplateIdentity(element))
            {
                removals.Add(element);
            }
        }

        for (int index = 0; index < removals.Count; index++)
        {
            removals[index].Remove();
        }
    }

    private static void RemoveElements(IHtmlCollection<IElement> elements)
    {
        List<IElement> removals = new(elements.Length);

        for (int index = 0; index < elements.Length; index++)
        {
            removals.Add(elements[index]);
        }

        for (int index = 0; index < removals.Count; index++)
        {
            removals[index].Remove();
        }
    }

    private IElement? SelectBestCandidate(IParentNode document)
    {
        IHtmlCollection<IElement> candidates = document.QuerySelectorAll(
            "article,main,[role='main'],section,div,body");
        IElement? selected = null;
        long selectedScore = long.MinValue;
        int count = Math.Min(candidates.Length, MaximumCandidates);

        for (int index = 0; index < count; index++)
        {
            IElement candidate = candidates[index];
            string normalizedText = NormalizeInline(candidate.TextContent);

            if (normalizedText.Length < _minimumCharacters / 2)
            {
                continue;
            }

            int linkTextLength = GetLinkTextLength(candidate);
            int paragraphCount = candidate.QuerySelectorAll(
                "p,li,blockquote,pre").Length;
            long score = normalizedText.Length
                - (long)linkTextLength * 2
                + (long)Math.Min(paragraphCount, 100) * 80
                + GetSemanticBonus(candidate);

            if (score > selectedScore)
            {
                selected = candidate;
                selectedScore = score;
            }
        }

        return selected;
    }

    private string BuildStructuredText(IElement candidate)
    {
        IHtmlCollection<IElement> blocks = candidate.QuerySelectorAll(
            "h1,h2,h3,h4,h5,h6,p,li,blockquote,pre,figcaption");
        StringBuilder builder = new(Math.Min(_maximumCharacters, 16 * 1024));
        HashSet<string> seen = new(StringComparer.Ordinal);

        for (int index = 0; index < blocks.Length; index++)
        {
            string line = NormalizeInline(blocks[index].TextContent);

            if (line.Length == 0
                || IsBoilerplateLine(line)
                || !seen.Add(line))
            {
                continue;
            }

            AppendBoundedLine(builder, line);

            if (builder.Length >= _maximumCharacters)
            {
                break;
            }
        }

        if (builder.Length == 0)
        {
            AppendBoundedLine(builder, NormalizeInline(candidate.TextContent));
        }

        return builder.ToString().Trim();
    }

    private void AppendBoundedLine(StringBuilder builder, string line)
    {
        if (line.Length == 0 || builder.Length >= _maximumCharacters)
        {
            return;
        }

        int separatorLength = builder.Length == 0 ? 0 : 2;
        int available = _maximumCharacters - builder.Length - separatorLength;

        if (available <= 0)
        {
            return;
        }

        if (separatorLength > 0)
        {
            builder.AppendLine();
            builder.AppendLine();
        }

        if (line.Length <= available)
        {
            builder.Append(line);
            return;
        }

        int cut = line.LastIndexOf(' ', available - 1, available);

        if (cut < Math.Min(available / 2, 100))
        {
            cut = available;
        }

        builder.Append(line.AsSpan(0, cut));
    }

    private static bool HasBoilerplateIdentity(IElement element)
    {
        string identity = string.Concat(element.Id, " ", element.ClassName)
            .ToLowerInvariant();

        for (int index = 0; index < BoilerplateTokens.Length; index++)
        {
            if (identity.Contains(
                    BoilerplateTokens[index],
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBoilerplateLine(string line)
    {
        if (line.Length > 400)
        {
            return false;
        }

        string lower = line.ToLowerInvariant();

        for (int index = 0; index < BoilerplateLineFragments.Length; index++)
        {
            if (lower.Contains(
                    BoilerplateLineFragments[index],
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetLinkTextLength(IElement candidate)
    {
        IHtmlCollection<IElement> links = candidate.QuerySelectorAll("a");
        int total = 0;

        for (int index = 0; index < links.Length; index++)
        {
            total += NormalizeInline(links[index].TextContent).Length;
        }

        return total;
    }

    private static int GetSemanticBonus(IElement candidate)
    {
        if (candidate.TagName.Equals("ARTICLE", StringComparison.OrdinalIgnoreCase))
        {
            return 5000;
        }

        if (candidate.TagName.Equals("MAIN", StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                candidate.GetAttribute("role"),
                "main",
                StringComparison.OrdinalIgnoreCase))
        {
            return 3000;
        }

        if (candidate.TagName.Equals("BODY", StringComparison.OrdinalIgnoreCase))
        {
            return -1000;
        }

        return 0;
    }

    private static string NormalizeInline(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new(value.Length);
        bool previousWhitespace = true;

        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];

            if (char.IsWhiteSpace(character))
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                    previousWhitespace = true;
                }

                continue;
            }

            builder.Append(character);
            previousWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    private static string? NormalizeOptionalLine(string? value)
    {
        string normalized = NormalizeInline(value ?? string.Empty);
        return normalized.Length == 0
            ? null
            : normalized.Length <= 300
                ? normalized
                : normalized[..300];
    }
}
