namespace SignalRadar.Bot.Discord;

public sealed record DiscordEmbedEnvelope(
    string? Title,
    string? Description,
    string? Url);
