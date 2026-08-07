using System.Collections.Frozen;

namespace SignalRadar.Bot.Discord;

public sealed class DiscordInboxOptions
{
    public DiscordInboxOptions(
        string botToken,
        IEnumerable<ulong> allowedGuildIds,
        IEnumerable<ulong> allowedChannelIds,
        IEnumerable<ulong>? allowedAuthorIds = null,
        bool requireAutomatedAuthor = true,
        string sourceName = "discord")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(botToken);
        ArgumentNullException.ThrowIfNull(allowedGuildIds);
        ArgumentNullException.ThrowIfNull(allowedChannelIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        BotToken = botToken;
        AllowedGuildIds = CreateRequiredSet(allowedGuildIds, nameof(allowedGuildIds));
        AllowedChannelIds = CreateRequiredSet(allowedChannelIds, nameof(allowedChannelIds));
        AllowedAuthorIds = CreateOptionalSet(allowedAuthorIds);
        RequireAutomatedAuthor = requireAutomatedAuthor;
        SourceName = sourceName.Trim();
    }

    public string BotToken { get; }

    public FrozenSet<ulong> AllowedGuildIds { get; }

    public FrozenSet<ulong> AllowedChannelIds { get; }

    public FrozenSet<ulong> AllowedAuthorIds { get; }

    public bool RequireAutomatedAuthor { get; }

    public string SourceName { get; }

    private static FrozenSet<ulong> CreateRequiredSet(
        IEnumerable<ulong> values,
        string parameterName)
    {
        ulong[] ids = values.Distinct().ToArray();

        if (ids.Length == 0 || ids.Any(static id => id == 0))
        {
            throw new ArgumentException(
                "At least one non-zero Discord snowflake is required.",
                parameterName);
        }

        return ids.ToFrozenSet();
    }

    private static FrozenSet<ulong> CreateOptionalSet(IEnumerable<ulong>? values)
    {
        if (values is null)
        {
            return Array.Empty<ulong>().ToFrozenSet();
        }

        ulong[] ids = values.Distinct().ToArray();

        if (ids.Any(static id => id == 0))
        {
            throw new ArgumentException(
                "Discord snowflakes must be non-zero.",
                nameof(values));
        }

        return ids.ToFrozenSet();
    }
}
