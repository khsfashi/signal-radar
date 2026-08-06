using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using SignalRadar.Application.Feeds;

namespace SignalRadar.Infrastructure.Feeds;

public sealed class RssAtomFeedParser : IFeedDocumentParser
{
    private const long MaximumXmlCharacters = 4L * 1024L * 1024L;
    private const int MaximumItems = 500;

    public IReadOnlyList<ParsedFeedItem> Parse(
        byte[] content,
        Uri feedUrl,
        DateTimeOffset fetchedAt)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(feedUrl);

        if (content.Length == 0)
        {
            throw new InvalidDataException("The feed response was empty.");
        }

        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumXmlCharacters,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };

        using MemoryStream stream = new(content, writable: false);
        using XmlReader reader = XmlReader.Create(stream, settings);
        XDocument document = XDocument.Load(reader, LoadOptions.None);
        XElement root = document.Root
            ?? throw new InvalidDataException("The feed XML has no root element.");

        List<ParsedFeedItem> items = root.Name.LocalName.Equals(
            "feed",
            StringComparison.OrdinalIgnoreCase)
            ? ParseAtom(root, feedUrl, fetchedAt)
            : ParseRss(root, feedUrl, fetchedAt);

        if (items.Count == 0)
        {
            throw new InvalidDataException(
                "The document did not contain any usable RSS or Atom entries.");
        }

        return items;
    }

    private static List<ParsedFeedItem> ParseAtom(
        XElement root,
        Uri feedUrl,
        DateTimeOffset fetchedAt)
    {
        List<ParsedFeedItem> items = [];

        foreach (XElement entry in root.Elements())
        {
            if (!entry.Name.LocalName.Equals("entry", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? title = GetChildValue(entry, "title");
            string? link = GetAtomLink(entry);

            if (!TryCreateItem(
                    title,
                    link,
                    GetChildValue(entry, "published")
                        ?? GetChildValue(entry, "updated"),
                    feedUrl,
                    fetchedAt,
                    out ParsedFeedItem? item))
            {
                continue;
            }

            items.Add(item);

            if (items.Count >= MaximumItems)
            {
                break;
            }
        }

        return items;
    }

    private static List<ParsedFeedItem> ParseRss(
        XElement root,
        Uri feedUrl,
        DateTimeOffset fetchedAt)
    {
        List<ParsedFeedItem> items = [];

        foreach (XElement element in root.Descendants())
        {
            if (!element.Name.LocalName.Equals("item", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? link = GetChildValue(element, "link");

            if (string.IsNullOrWhiteSpace(link))
            {
                XElement? guid = FindChild(element, "guid");
                string? isPermaLink = guid?.Attribute("isPermaLink")?.Value;

                if (guid is not null
                    && !string.Equals(isPermaLink, "false", StringComparison.OrdinalIgnoreCase))
                {
                    link = guid.Value;
                }
            }

            if (!TryCreateItem(
                    GetChildValue(element, "title"),
                    link,
                    GetChildValue(element, "pubDate")
                        ?? GetChildValue(element, "date")
                        ?? GetChildValue(element, "updated"),
                    feedUrl,
                    fetchedAt,
                    out ParsedFeedItem? item))
            {
                continue;
            }

            items.Add(item);

            if (items.Count >= MaximumItems)
            {
                break;
            }
        }

        return items;
    }

    private static bool TryCreateItem(
        string? title,
        string? link,
        string? publishedAt,
        Uri feedUrl,
        DateTimeOffset fetchedAt,
        [NotNullWhen(true)] out ParsedFeedItem? item)
    {
        item = null;
        string normalizedTitle = NormalizeTitle(title);

        if (normalizedTitle.Length == 0
            || string.IsNullOrWhiteSpace(link)
            || !TryResolveLink(feedUrl, link, out Uri? resolvedLink))
        {
            return false;
        }

        DateTimeOffset parsedPublishedAt = DateTimeOffset.TryParse(
            publishedAt,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out DateTimeOffset parsed)
            ? parsed.ToUniversalTime()
            : fetchedAt.ToUniversalTime();

        item = new ParsedFeedItem(
            normalizedTitle,
            resolvedLink,
            parsedPublishedAt);
        return true;
    }

    private static bool TryResolveLink(
        Uri feedUrl,
        string link,
        [NotNullWhen(true)] out Uri? resolvedLink)
    {
        resolvedLink = null;

        if (!Uri.TryCreate(feedUrl, link.Trim(), out Uri? candidate)
            || candidate.Scheme is not ("http" or "https"))
        {
            return false;
        }

        resolvedLink = candidate;
        return true;
    }

    private static string? GetAtomLink(XElement entry)
    {
        string? fallback = null;

        foreach (XElement child in entry.Elements())
        {
            if (!child.Name.LocalName.Equals("link", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? href = child.Attribute("href")?.Value;

            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            string? relation = child.Attribute("rel")?.Value;

            if (string.IsNullOrWhiteSpace(relation)
                || relation.Equals("alternate", StringComparison.OrdinalIgnoreCase))
            {
                return href;
            }

            fallback ??= href;
        }

        return fallback;
    }

    private static string? GetChildValue(XElement parent, string localName)
    {
        return FindChild(parent, localName)?.Value;
    }

    private static XElement? FindChild(XElement parent, string localName)
    {
        foreach (XElement child in parent.Elements())
        {
            if (child.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return null;
    }

    private static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        string[] segments = title.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string normalized = string.Join(' ', segments);

        return normalized.Length <= 1000
            ? normalized
            : normalized[..1000];
    }
}
