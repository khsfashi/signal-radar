using System.Globalization;
using SignalRadar.Bot.Discord;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Worker.Publishing;

public sealed record DiscordTopicPublicationRoute(
    ArticleTopic Topic,
    ulong ChannelId,
    decimal MinimumScore,
    TimeSpan BatchWindow);

public sealed record DiscordAutomaticTopicPublishingOptions(
    IReadOnlyList<DiscordTopicPublicationRoute> Routes,
    decimal MinimumScore,
    int BatchSize,
    TimeSpan BatchWindow,
    TimeSpan PollInterval,
    TimeSpan LeaseDuration,
    TimeSpan RetryDelay)
{
    public const string PublicationKind = "discord-topic-article-v1";
}

public static class DiscordAutomaticTopicPublishingConfiguration
{
    public static DiscordAutomaticTopicPublishingOptions? Load(
        bool discordEnabled,
        IReadOnlyCollection<ulong> allowedChannelIds)
    {
        ArgumentNullException.ThrowIfNull(allowedChannelIds);

        if (!discordEnabled
            || !ParseBoolean(
                "DISCORD_TOPIC_PUBLISHING_ENABLED",
                defaultValue: true))
        {
            return null;
        }

        decimal minimumScore = ParseDecimal(
            "DISCORD_TOPIC_MIN_SCORE",
            defaultValue: 0m,
            minimum: 0m,
            maximum: 100m);
        int batchSize = ParseInteger(
            "DISCORD_TOPIC_BATCH_SIZE",
            defaultValue: 10,
            minimum: 1,
            maximum: 25);
        TimeSpan batchWindow = TimeSpan.FromMinutes(ParseInteger(
            "DISCORD_TOPIC_BATCH_WINDOW_MINUTES",
            defaultValue: 30,
            minimum: 5,
            maximum: 1440));
        IReadOnlyList<DiscordTopicPublicationRoute> routes = ParseRoutes(
            Environment.GetEnvironmentVariable("DISCORD_TOPIC_CHANNELS"),
            allowedChannelIds,
            minimumScore,
            batchWindow);
        TimeSpan pollInterval = TimeSpan.FromSeconds(ParseInteger(
            "DISCORD_TOPIC_POLL_SECONDS",
            defaultValue: 30,
            minimum: 10,
            maximum: 600));
        TimeSpan leaseDuration = TimeSpan.FromSeconds(ParseInteger(
            "DISCORD_TOPIC_LEASE_SECONDS",
            defaultValue: 120,
            minimum: 30,
            maximum: 3600));
        TimeSpan retryDelay = TimeSpan.FromSeconds(ParseInteger(
            "DISCORD_TOPIC_RETRY_SECONDS",
            defaultValue: 60,
            minimum: 10,
            maximum: 86400));

        return new DiscordAutomaticTopicPublishingOptions(
            routes,
            minimumScore,
            batchSize,
            batchWindow,
            pollInterval,
            leaseDuration,
            retryDelay);
    }

    private static IReadOnlyList<DiscordTopicPublicationRoute> ParseRoutes(
        string? value,
        IReadOnlyCollection<ulong> allowedChannelIds,
        decimal minimumScore,
        TimeSpan batchWindow)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        string[] segments = value.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        List<DiscordTopicPublicationRoute> routes = new(segments.Length);
        HashSet<ArticleTopic> configuredTopics = [];

        for (int index = 0; index < segments.Length; index++)
        {
            string segment = segments[index];
            int separatorIndex = segment.LastIndexOf(':');

            if (separatorIndex <= 0 || separatorIndex == segment.Length - 1)
            {
                throw new InvalidOperationException(
                    "DISCORD_TOPIC_CHANNELS entries must use 'topic:channelId'.");
            }

            string topicValue = segment[..separatorIndex].Trim();
            string channelValue = segment[(separatorIndex + 1)..].Trim();
            ArticleTopic topic = DiscordArticleInteractionCodec.ParseTopic(topicValue);
            int topicNumber = (int)topic;

            if (topic == ArticleTopic.None
                || topicNumber <= 0
                || (topicNumber & (topicNumber - 1)) != 0)
            {
                throw new InvalidOperationException(
                    $"Topic '{topicValue}' is not valid for automatic publishing.");
            }

            if (!ulong.TryParse(channelValue, out ulong channelId)
                || channelId == 0)
            {
                throw new InvalidOperationException(
                    $"Channel '{channelValue}' is not a valid Discord snowflake.");
            }

            if (allowedChannelIds.Count > 0
                && !allowedChannelIds.Contains(channelId))
            {
                throw new InvalidOperationException(
                    $"Legacy environment route channel {channelId} must also be listed in DISCORD_ALLOWED_CHANNEL_IDS. "
                        + "Use /route-set for runtime-managed routes instead.");
            }

            if (!configuredTopics.Add(topic))
            {
                throw new InvalidOperationException(
                    $"Automatic publishing topic '{topicValue}' is configured more than once.");
            }

            routes.Add(new DiscordTopicPublicationRoute(
                topic,
                channelId,
                minimumScore,
                batchWindow));
        }

        return routes;
    }

    private static bool ParseBoolean(string name, bool defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return bool.TryParse(value, out bool parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Environment variable '{name}' must be 'true' or 'false'.");
    }

    private static int ParseInteger(
        string name,
        int defaultValue,
        int minimum,
        int maximum)
    {
        string? value = Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            throw new InvalidOperationException(
                $"Environment variable '{name}' must be between {minimum} and {maximum}.");
        }

        return parsed;
    }

    private static decimal ParseDecimal(
        string name,
        decimal defaultValue,
        decimal minimum,
        decimal maximum)
    {
        string? value = Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!decimal.TryParse(
                value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            throw new InvalidOperationException(
                $"Environment variable '{name}' must be between {minimum} and {maximum}.");
        }

        return parsed;
    }
}
