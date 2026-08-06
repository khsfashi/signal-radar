using System.Diagnostics.CodeAnalysis;
using Discord;
using Discord.WebSocket;
using SignalRadar.Application.Digests;
using SignalRadar.Application.Publishing;

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
    private readonly DiscordArticleInteractionHandler? _interactionHandler;
    private readonly DiscordSocketClient _client;
    private readonly Action<string> _log;
    private readonly TaskCompletionSource<bool> _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;

    public DiscordInboxGateway(
        DiscordInboxOptions options,
        DiscordInboxProcessor processor,
        DiscordSocketMessageMapper mapper,
        Action<string>? log = null,
        DiscordArticleInteractionHandler? interactionHandler = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _interactionHandler = interactionHandler;
        _log = log ?? (static _ => { });

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds
                | GatewayIntents.GuildMessages
                | GatewayIntents.MessageContent,
            MessageCacheSize = 0
        });

        _client.Log += HandleLogAsync;
        _client.Ready += HandleReadyAsync;
        _client.MessageReceived += HandleMessageAsync;

        if (_interactionHandler is not null)
        {
            _client.SlashCommandExecuted += HandleSlashCommandAsync;
            _client.ButtonExecuted += HandleButtonAsync;
        }
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

    public async ValueTask<ulong> SendDigestAsync(
        ulong channelId,
        ArticleDigest digest,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(digest);

        if (!_options.AllowedChannelIds.Contains(channelId))
        {
            throw new InvalidOperationException(
                "Scheduled digest channel is not in DISCORD_ALLOWED_CHANNEL_IDS.");
        }

        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        IMessageChannel channel = _client.GetChannel(channelId) as IMessageChannel
            ?? throw new InvalidOperationException(
                $"Discord channel {channelId} is unavailable or cannot receive messages.");
        IUserMessage message = await channel.SendMessageAsync(
            text: DiscordDigestMessageFactory.GetHeading(digest),
            embed: DiscordDigestMessageFactory.BuildEmbed(digest),
            allowedMentions: AllowedMentions.None,
            options: new RequestOptions
            {
                CancelToken = cancellationToken
            }).ConfigureAwait(false);
        return message.Id;
    }

    public async ValueTask<ulong> SendAutomaticTopicArticleAsync(
        ulong channelId,
        AutomaticTopicPublicationCandidate article,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(article);

        if (!_options.AllowedChannelIds.Contains(channelId))
        {
            throw new InvalidOperationException(
                "Automatic topic channel is not in DISCORD_ALLOWED_CHANNEL_IDS.");
        }

        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        SocketChannel channel = _client.GetChannel(channelId)
            ?? throw new InvalidOperationException(
                $"Discord channel {channelId} is unavailable.");
        RequestOptions requestOptions = new()
        {
            CancelToken = cancellationToken
        };
        Embed embed = DiscordAutomaticTopicMessageFactory.BuildEmbed(article);
        MessageComponent components =
            DiscordAutomaticTopicMessageFactory.BuildComponents(article);

        if (channel is IForumChannel forumChannel)
        {
            IThreadChannel thread = await forumChannel.CreatePostAsync(
                DiscordAutomaticTopicMessageFactory.GetForumTitle(article),
                text: DiscordAutomaticTopicMessageFactory.GetHeading(article),
                embed: embed,
                options: requestOptions,
                allowedMentions: AllowedMentions.None,
                components: components).ConfigureAwait(false);
            return thread.Id;
        }

        if (channel is not IMessageChannel messageChannel)
        {
            throw new InvalidOperationException(
                $"Discord channel {channelId} cannot receive public article messages.");
        }

        IUserMessage message = await messageChannel.SendMessageAsync(
            text: DiscordAutomaticTopicMessageFactory.GetHeading(article),
            embed: embed,
            allowedMentions: AllowedMentions.None,
            components: components,
            options: requestOptions).ConfigureAwait(false);
        return message.Id;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_interactionHandler is not null)
        {
            _client.ButtonExecuted -= HandleButtonAsync;
            _client.SlashCommandExecuted -= HandleSlashCommandAsync;
        }

        _client.MessageReceived -= HandleMessageAsync;
        _client.Ready -= HandleReadyAsync;
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

    private async Task HandleReadyAsync()
    {
        _ready.TrySetResult(true);

        if (_interactionHandler is not null)
        {
            await _interactionHandler.RegisterCommandsAsync(_client)
                .ConfigureAwait(false);
        }
    }

    private Task HandleSlashCommandAsync(SocketSlashCommand command)
    {
        return _interactionHandler?.HandleSlashCommandAsync(command)
            ?? Task.CompletedTask;
    }

    private Task HandleButtonAsync(SocketMessageComponent component)
    {
        return _interactionHandler?.HandleButtonAsync(component)
            ?? Task.CompletedTask;
    }

    private Task HandleLogAsync(LogMessage message)
    {
        _log(message.ToString());
        return Task.CompletedTask;
    }
}
