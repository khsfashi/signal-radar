using System.Text.RegularExpressions;
using SignalRadar.Application.Articles;

namespace SignalRadar.Bot.Discord;

public sealed partial class DiscordMessageArticleCandidateFactory
{
    public bool TryCreate(
        DiscordMessageEnvelope message,
        string source,
        out CollectedArticleCandidate? candidate)
    {
        ArgumentNullException.ThrowIfNull(message);
        candidate = null;

        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        foreach (DiscordEmbedEnvelope embed in message.Embeds)
        {
            if (TryExtractEmbed(embed, out string? title, out string? url))
            {
                candidate = new CollectedArticleCandidate(
                    title,
                    url,
                    source.Trim(),
                    message.PublishedAt);
                return true;
            }
        }

        if (!TryExtractText(message.Content, out string? contentTitle, out string? contentUrl))
        {
            return false;
        }

        candidate = new CollectedArticleCandidate(
            contentTitle,
            contentUrl,
            source.Trim(),
            message.PublishedAt);
        return true;
    }

    private static bool TryExtractEmbed(
        DiscordEmbedEnvelope embed,
        out string? title,
        out string? url)
    {
        title = null;
        url = NormalizeExplicitUrl(embed.Url);

        if (url is null)
        {
            string searchableText = string.Concat(
                embed.Title,
                Environment.NewLine,
                embed.Description);

            if (!TryFindUrl(searchableText, out url))
            {
                return false;
            }
        }

        title = FirstNonEmpty(
            embed.Title,
            FindTitle(embed.Description ?? string.Empty, url),
            url);
        return true;
    }

    private static bool TryExtractText(
        string messageContent,
        out string? title,
        out string? url)
    {
        title = null;
        url = null;

        if (string.IsNullOrWhiteSpace(messageContent)
            || !TryFindUrl(messageContent, out url))
        {
            return false;
        }

        title = FirstNonEmpty(FindTitle(messageContent, url), url);
        return true;
    }

    private static bool TryFindUrl(string text, out string? url)
    {
        Match match = HttpUrlRegex().Match(text);

        if (!match.Success)
        {
            url = null;
            return false;
        }

        url = TrimTrailingPunctuation(match.Value);
        return true;
    }

    private static string? NormalizeExplicitUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        string trimmed = TrimTrailingPunctuation(url.Trim());

        return Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
                ? trimmed
                : null;
    }

    private static string? FindTitle(string messageContent, string matchedUrl)
    {
        string[] lines = messageContent.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string line in lines)
        {
            if (!line.Contains(matchedUrl, StringComparison.Ordinal)
                && !HttpUrlRegex().IsMatch(line))
            {
                return line;
            }
        }

        return null;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        throw new InvalidOperationException("A non-empty fallback value is required.");
    }

    private static string TrimTrailingPunctuation(string url)
    {
        return url.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}');
    }

    [GeneratedRegex(
        @"https?://[^\s<>()]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HttpUrlRegex();
}
