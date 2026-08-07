using System.Text;
using SignalRadar.Application.Feeds;
using SignalRadar.Infrastructure.Feeds;
using Xunit;

namespace SignalRadar.Infrastructure.Tests.Feeds;

public sealed class RssAtomFeedParserTests
{
    [Fact]
    public void Parse_ReadsRssAndResolvesRelativeLinks()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <rss version="2.0">
              <channel>
                <title>Example</title>
                <item>
                  <title>  New   AI tool  </title>
                  <link>/posts/ai-tool</link>
                  <pubDate>Thu, 06 Aug 2026 01:02:03 GMT</pubDate>
                </item>
              </channel>
            </rss>
            """;
        RssAtomFeedParser parser = new();

        IReadOnlyList<ParsedFeedItem> items = parser.Parse(
            Encoding.UTF8.GetBytes(xml),
            new Uri("https://example.com/feed.xml"),
            DateTimeOffset.UtcNow);

        ParsedFeedItem item = Assert.Single(items);
        Assert.Equal("New AI tool", item.Title);
        Assert.Equal("https://example.com/posts/ai-tool", item.Url.AbsoluteUri);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 6, 1, 2, 3, TimeSpan.Zero),
            item.PublishedAt);
    }

    [Fact]
    public void Parse_ReadsAtomAlternateLinkAndUpdatedDate()
    {
        const string xml = """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <title>Example</title>
              <entry>
                <title>Engine release</title>
                <link rel="self" href="https://example.com/api/1" />
                <link rel="alternate" href="https://example.com/releases/1" />
                <updated>2026-08-06T09:30:00+09:00</updated>
              </entry>
            </feed>
            """;
        RssAtomFeedParser parser = new();

        ParsedFeedItem item = Assert.Single(parser.Parse(
            Encoding.UTF8.GetBytes(xml),
            new Uri("https://example.com/atom.xml"),
            DateTimeOffset.UtcNow));

        Assert.Equal("https://example.com/releases/1", item.Url.AbsoluteUri);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 6, 0, 30, 0, TimeSpan.Zero),
            item.PublishedAt);
    }
}
