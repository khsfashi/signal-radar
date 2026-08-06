using System.Text.RegularExpressions;
using SignalRadar.Application.Articles;

namespace SignalRadar.Bot.Discord;

public sealed partial class DiscordMessageArticleCandidateFactory
{
    public bool TryCreate(
        string messageContent,
        string source,
        DateTimeOffset publishedAt,
        out CollectedArticleCandidate? candidate)
    {
        candidate = null;

        if (string.IsNullOrWhiteSpace(messageContent)
            || string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        Match urlMatch = HttpUrlRegex().Match(messageContent);

        if (!urlMatch.Success)
        {
            return false;
        }

        string url = TrimTrailingPunctuation(urlMatch.Value);
        string title = FindTitle(messageContent, urlMatch.Value);

        candidate = new CollectedArticleCandidate(
            title,
            url,
            source.Trim(),
            publishedAt);

        return true;
    }

    private static string FindTitle(string messageContent, string matchedUrl)
    {
        string[] lines = messageContent.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string line in lines)
        {
            if (!line.Contains(matchedUrl, StringComparison.Ordinal))
            {
                return line;
            }
        }

        return matchedUrl;
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
