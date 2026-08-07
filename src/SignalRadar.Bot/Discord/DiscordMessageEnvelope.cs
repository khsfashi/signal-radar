namespace SignalRadar.Bot.Discord;

public sealed record DiscordMessageEnvelope(
    ulong MessageId,
    ulong GuildId,
    ulong ChannelId,
    ulong AuthorId,
    bool AuthorIsBot,
    bool AuthorIsWebhook,
    string Content,
    IReadOnlyList<DiscordEmbedEnvelope> Embeds,
    DateTimeOffset PublishedAt);
