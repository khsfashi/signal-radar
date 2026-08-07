using System.Globalization;
using SignalRadar.Bot.Discord;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Worker.Digests;

public static class DiscordDigestScheduleConfiguration
{
    public static DiscordDigestScheduleOptions? Load(
        bool discordEnabled,
        IReadOnlyCollection<ulong> allowedChannelIds)
    {
        bool dailyEnabled = ParseBoolean(
            "DIGEST_DAILY_ENABLED",
            defaultValue: false);
        bool weeklyEnabled = ParseBoolean(
            "DIGEST_WEEKLY_ENABLED",
            defaultValue: false);

        if (!dailyEnabled && !weeklyEnabled)
        {
            return null;
        }

        if (!discordEnabled)
        {
            throw new InvalidOperationException(
                "Scheduled digests require DISCORD_ENABLED=true.");
        }

        ulong channelId = ParseSnowflake("DIGEST_CHANNEL_ID");

        if (!allowedChannelIds.Contains(channelId))
        {
            throw new InvalidOperationException(
                "DIGEST_CHANNEL_ID must also be listed in DISCORD_ALLOWED_CHANNEL_IDS.");
        }

        ulong actorUserId = ParseSnowflake("DIGEST_ACTOR_USER_ID");
        string timeZoneId = Environment.GetEnvironmentVariable("DIGEST_TIME_ZONE")
            ?? "Asia/Seoul";
        TimeZoneInfo timeZone;

        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new InvalidOperationException(
                $"DIGEST_TIME_ZONE '{timeZoneId}' was not found.",
                exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new InvalidOperationException(
                $"DIGEST_TIME_ZONE '{timeZoneId}' is invalid.",
                exception);
        }

        string? topicValue = Environment.GetEnvironmentVariable("DIGEST_TOPIC");
        ArticleTopic topic = DiscordArticleInteractionCodec.ParseTopic(topicValue);
        return new DiscordDigestScheduleOptions(
            channelId,
            actorUserId,
            timeZone,
            dailyEnabled,
            ParseClock("DIGEST_DAILY_TIME", "08:00"),
            weeklyEnabled,
            ParseDayOfWeek("DIGEST_WEEKLY_DAY", DayOfWeek.Monday),
            ParseClock("DIGEST_WEEKLY_TIME", "08:00"),
            topic,
            ParseInteger("DIGEST_LIMIT", 10, 1, 10),
            TimeSpan.FromSeconds(ParseInteger(
                "DIGEST_POLL_SECONDS",
                60,
                10,
                600)),
            TimeSpan.FromSeconds(ParseInteger(
                "DIGEST_LEASE_SECONDS",
                300,
                30,
                3600)));
    }

    private static ulong ParseSnowflake(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);

        return ulong.TryParse(value, out ulong parsed) && parsed != 0
            ? parsed
            : throw new InvalidOperationException(
                $"Required environment variable '{name}' must be a Discord snowflake.");
    }

    private static TimeSpan ParseClock(string name, string defaultValue)
    {
        string value = Environment.GetEnvironmentVariable(name) ?? defaultValue;
        return TimeSpan.TryParseExact(
            value.Trim(),
            "hh\\:mm",
            CultureInfo.InvariantCulture,
            out TimeSpan parsed)
            && parsed >= TimeSpan.Zero
            && parsed < TimeSpan.FromDays(1)
                ? parsed
                : throw new InvalidOperationException(
                    $"Environment variable '{name}' must use HH:mm.");
    }

    private static DayOfWeek ParseDayOfWeek(
        string name,
        DayOfWeek defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out DayOfWeek parsed)
            ? parsed
            : throw new InvalidOperationException(
                $"Environment variable '{name}' must be a day name such as Monday.");
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

        return int.TryParse(value, out int parsed)
            && parsed >= minimum
            && parsed <= maximum
                ? parsed
                : throw new InvalidOperationException(
                    $"Environment variable '{name}' must be between {minimum} and {maximum}.");
    }
}
