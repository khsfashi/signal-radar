using System.Diagnostics.CodeAnalysis;
using Discord;
using Discord.WebSocket;

namespace SignalRadar.Bot.Discord;

public sealed class DiscordSocketMessageMapper
{
    public bool TryMap(
        SocketMessage message,
        [NotNullWhen(true)] out DiscordMessageEnvelope? envelope)
    {
        ArgumentNullException.ThrowIfNull(message);
        envelope = null;

        if (message.Channel is not SocketGuildChannel guildChannel)
        {
            return false;
        }

        List<DiscordEmbedEnvelope> embeds = new(message.Embeds.Count);

        foreach (IEmbed embed in message.Embeds)
        {
            embeds.Add(new DiscordEmbedEnvelope(
                embed.Title,
                embed.Description,
                embed.Url));
        }

        envelope = new DiscordMessageEnvelope(
            message.Id,
            guildChannel.Guild.Id,
            guildChannel.Id,
            message.Author.Id,
            message.Author.IsBot,
            message.Author.IsWebhook,
            message.Content,
            embeds,
            message.Timestamp);

        return true;
    }
}

public sealed class DiscordInboxGateway : IDisposable
{
    private readonly DiscordInboxOptions _options;
    private readonly DiscordInboxProcessor _processor;
    private readonly DiscordSocketMessageMapper _mapper;
    private readonly DiscordSocketClient _client;
    private readonly Action<string> _log;
    private bool _disposed;

    public DiscordInboxGateway(
        DiscordInboxOptions options,
        DiscordInboxProcessor processor,
        DiscordSocketMessageMapper mapper,
        Action<string>? log = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _log = log ?? (static _ => { });

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds
                | GatewayIntents.GuildMessages
                | GatewayIntents.MessageContent,
            MessageCacheSize = 0
        });

        _client.Log += HandleLogAsync;
        _client.MessageReceived += HandleMessageAsync;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _client
            .LoginAsync(TokenType.Bot, _options.BotToken)
            .ConfigureAwait(false);
        await _client.StartAsync().ConfigureAwait(false);
        _log("Discord inbox gateway started.");

        try
        {
            await Task
                .Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            await _client.StopAsync().ConfigureAwait(false);
            await _client.LogoutAsync().ConfigureAwait(false);
            _log("Discord inbox gateway stopped.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _client.MessageReceived -= HandleMessageAsync;
        _client.Log -= HandleLogAsync;
        _client.Dispose();
        _disposed = true;
    }

    private async Task HandleMessageAsync(SocketMessage socketMessage)
    {
        if (!_mapper.TryMap(socketMessage, out DiscordMessageEnvelope? message))
        {
            return;
        }

        try
        {
            DiscordInboxResult result = await _processor
                .ProcessAsync(message)
                .ConfigureAwait(false);

            _log($"DiscordMessage={result.MessageId}, Status={result.Status}");
        }
        catch (Exception exception)
        {
            _log($"DiscordMessage={message.MessageId}, Error={exception}");
        }
    }

    private Task HandleLogAsync(LogMessage message)
    {
        _log(message.ToString());
        return Task.CompletedTask;
    }
}
