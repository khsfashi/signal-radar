using System.Globalization;
using System.Text;
using Discord;
using Discord.WebSocket;
using SignalRadar.Application.Feeds;
using SignalRadar.Application.Preferences;
using SignalRadar.Application.Publishing;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Bot.Discord;

public sealed class DiscordManagementCommandHandler
{
    private static readonly HashSet<string> CommandNames = new(StringComparer.Ordinal)
    {
        "feed-add",
        "feed-list",
        "feed-enable",
        "feed-disable",
        "route-set",
        "route-list",
        "route-remove",
        "source-mute",
        "source-unmute",
        "source-muted"
    };

    private readonly DiscordInboxOptions _options;
    private readonly IFeedSourceAdministrationStore _feedStore;
    private readonly IDiscordTopicRouteStore _routeStore;
    private readonly ISourcePreferenceStore _preferenceStore;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _registrationLock = new(1, 1);
    private bool _registered;

    public DiscordManagementCommandHandler(
        DiscordInboxOptions options,
        IFeedSourceAdministrationStore feedStore,
        IDiscordTopicRouteStore routeStore,
        ISourcePreferenceStore preferenceStore,
        Action<string>? log = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _feedStore = feedStore ?? throw new ArgumentNullException(nameof(feedStore));
        _routeStore = routeStore ?? throw new ArgumentNullException(nameof(routeStore));
        _preferenceStore = preferenceStore
            ?? throw new ArgumentNullException(nameof(preferenceStore));
        _log = log ?? (static _ => { });
    }

    public bool CanHandle(string commandName) => CommandNames.Contains(commandName);

    public async Task RegisterAsync(DiscordSocketClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        await _registrationLock.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_registered)
            {
                return;
            }

            SlashCommandProperties[] commands = BuildCommands();

            foreach (ulong guildId in _options.AllowedGuildIds)
            {
                SocketGuild? guild = client.GetGuild(guildId);

                if (guild is null)
                {
                    _log($"Discord management command guild {guildId} is unavailable.");
                    continue;
                }

                for (int index = 0; index < commands.Length; index++)
                {
                    await guild.CreateApplicationCommandAsync(commands[index])
                        .ConfigureAwait(false);
                }
            }

            _registered = true;
            _log("Discord feed, route, and source preference commands synchronized.");
        }
        finally
        {
            _registrationLock.Release();
        }
    }

    public async Task HandleAsync(SocketSlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!command.GuildId.HasValue
            || !_options.AllowedGuildIds.Contains(command.GuildId.Value))
        {
            await command.RespondAsync(
                "이 명령은 Signal Radar가 허용된 서버에서만 사용할 수 있습니다.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        try
        {
            switch (command.Data.Name)
            {
                case "feed-add":
                    RequireManager(command);
                    await HandleFeedAddAsync(command).ConfigureAwait(false);
                    break;
                case "feed-list":
                    RequireManager(command);
                    await HandleFeedListAsync(command).ConfigureAwait(false);
                    break;
                case "feed-enable":
                    RequireManager(command);
                    await HandleFeedEnabledAsync(command, true).ConfigureAwait(false);
                    break;
                case "feed-disable":
                    RequireManager(command);
                    await HandleFeedEnabledAsync(command, false).ConfigureAwait(false);
                    break;
                case "route-set":
                    RequireManager(command);
                    await HandleRouteSetAsync(command).ConfigureAwait(false);
                    break;
                case "route-list":
                    RequireManager(command);
                    await HandleRouteListAsync(command).ConfigureAwait(false);
                    break;
                case "route-remove":
                    RequireManager(command);
                    await HandleRouteRemoveAsync(command).ConfigureAwait(false);
                    break;
                case "source-mute":
                    await HandleSourceMuteAsync(command, true).ConfigureAwait(false);
                    break;
                case "source-unmute":
                    await HandleSourceMuteAsync(command, false).ConfigureAwait(false);
                    break;
                case "source-muted":
                    await HandleMutedSourcesAsync(command).ConfigureAwait(false);
                    break;
                default:
                    await command.RespondAsync(
                        "지원하지 않는 관리 명령입니다.",
                        ephemeral: true).ConfigureAwait(false);
                    break;
            }
        }
        catch (UnauthorizedAccessException)
        {
            await command.RespondAsync(
                "서버 관리자 또는 서버 관리 권한이 필요한 명령입니다.",
                ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _log($"Discord management command {command.Data.Name} failed: {exception}");

            if (!command.HasResponded)
            {
                await command.RespondAsync(
                    "명령을 처리하지 못했습니다. `/status`와 Worker 로그를 확인해 주세요.",
                    ephemeral: true).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleFeedAddAsync(SocketSlashCommand command)
    {
        string name = GetRequiredString(command, "name");
        string url = GetRequiredString(command, "url");
        int minutes = GetInteger(command, "minutes", 30);

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? feedUrl)
            || feedUrl.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("RSS/Atom URL must be absolute HTTP(S).", nameof(url));
        }

        FeedSourceDefinition definition = new(
            name,
            feedUrl,
            TimeSpan.FromMinutes(minutes),
            enabled: true);
        await _feedStore.UpsertAsync(definition, CancellationToken.None)
            .ConfigureAwait(false);
        await command.RespondAsync(
            $"Feed `{definition.Name}` 구독을 저장했습니다. 다음 polling부터 적용됩니다.",
            ephemeral: true).ConfigureAwait(false);
    }

    private async Task HandleFeedListAsync(SocketSlashCommand command)
    {
        IReadOnlyList<ManagedFeedSource> sources = await _feedStore
            .GetAllAsync(CancellationToken.None).ConfigureAwait(false);
        StringBuilder text = new();

        for (int index = 0; index < sources.Count && index < 30; index++)
        {
            ManagedFeedSource source = sources[index];
            text.Append(source.Enabled ? "✅ " : "⏸️ ")
                .Append('`').Append(source.Name).Append("` · ")
                .Append(source.PollingInterval.TotalMinutes.ToString("0", CultureInfo.InvariantCulture))
                .Append("분\n")
                .Append(source.FeedUrl.AbsoluteUri)
                .AppendLine();
        }

        if (sources.Count > 30)
        {
            text.Append($"\n…외 {sources.Count - 30}개");
        }

        await command.RespondAsync(
            sources.Count == 0 ? "등록된 Feed가 없습니다." : text.ToString(),
            ephemeral: true,
            allowedMentions: AllowedMentions.None).ConfigureAwait(false);
    }

    private async Task HandleFeedEnabledAsync(
        SocketSlashCommand command,
        bool enabled)
    {
        string name = GetRequiredString(command, "name");
        bool changed = await _feedStore.SetEnabledAsync(
            name,
            enabled,
            CancellationToken.None).ConfigureAwait(false);
        await command.RespondAsync(
            changed
                ? $"`{name}` Feed를 {(enabled ? "활성화" : "비활성화")}했습니다."
                : $"`{name}` Feed를 찾지 못했습니다.",
            ephemeral: true).ConfigureAwait(false);
    }

    private async Task HandleRouteSetAsync(SocketSlashCommand command)
    {
        ArticleTopic topic = DiscordArticleInteractionCodec.ParseTopic(
            GetRequiredString(command, "topic"));

        if (topic == ArticleTopic.None)
        {
            throw new ArgumentException("A concrete topic is required.");
        }

        SocketGuildChannel channel = GetRequiredChannel(command, "channel");

        if (channel.Guild.Id != command.GuildId)
        {
            throw new ArgumentException("The destination channel must be in this server.");
        }

        int batchMinutes = GetInteger(command, "batch-minutes", 30);
        decimal minimumScore = GetDecimal(command, "min-score", 0m);
        ManagedDiscordTopicRoute route = new(
            topic,
            channel.Id,
            minimumScore,
            TimeSpan.FromMinutes(batchMinutes));
        await _routeStore.UpsertAsync(route, CancellationToken.None)
            .ConfigureAwait(false);
        await command.RespondAsync(
            $"{DiscordArticleInteractionCodec.GetTopicLabel(topic)} → <#{channel.Id}> · "
                + $"{batchMinutes}분 묶음 · 최소 점수 {minimumScore:0.#} 로 저장했습니다.",
            ephemeral: true,
            allowedMentions: AllowedMentions.None).ConfigureAwait(false);
    }

    private async Task HandleRouteListAsync(SocketSlashCommand command)
    {
        IReadOnlyList<ManagedDiscordTopicRoute> routes = await _routeStore
            .GetAllAsync(CancellationToken.None).ConfigureAwait(false);
        StringBuilder text = new();

        for (int index = 0; index < routes.Count; index++)
        {
            ManagedDiscordTopicRoute route = routes[index];
            text.Append(route.Enabled ? "✅ " : "⏸️ ")
                .Append(DiscordArticleInteractionCodec.GetTopicLabel(route.Topic))
                .Append(" → <#").Append(route.ChannelId).Append("> · ")
                .Append(route.BatchWindow.TotalMinutes.ToString("0", CultureInfo.InvariantCulture))
                .Append("분 · ≥")
                .Append(route.MinimumScore.ToString("0.#", CultureInfo.InvariantCulture))
                .AppendLine();
        }

        await command.RespondAsync(
            routes.Count == 0
                ? "런타임 라우트가 없습니다. `/route-set`으로 추가할 수 있습니다."
                : text.ToString(),
            ephemeral: true,
            allowedMentions: AllowedMentions.None).ConfigureAwait(false);
    }

    private async Task HandleRouteRemoveAsync(SocketSlashCommand command)
    {
        ArticleTopic topic = DiscordArticleInteractionCodec.ParseTopic(
            GetRequiredString(command, "topic"));
        bool removed = await _routeStore.RemoveAsync(topic, CancellationToken.None)
            .ConfigureAwait(false);
        await command.RespondAsync(
            removed
                ? $"{DiscordArticleInteractionCodec.GetTopicLabel(topic)} 런타임 라우트를 삭제했습니다."
                : "삭제할 런타임 라우트가 없습니다.",
            ephemeral: true).ConfigureAwait(false);
    }

    private async Task HandleSourceMuteAsync(
        SocketSlashCommand command,
        bool mute)
    {
        string source = GetRequiredString(command, "source");
        string actorId = DiscordArticleInteractionCodec.CreateActorId(command.User.Id);
        bool changed = mute
            ? await _preferenceStore.MuteAsync(
                actorId,
                source,
                CancellationToken.None).ConfigureAwait(false)
            : await _preferenceStore.UnmuteAsync(
                actorId,
                source,
                CancellationToken.None).ConfigureAwait(false);
        await command.RespondAsync(
            mute
                ? (changed
                    ? $"내 개인 조회에서 `{source}` 소스를 제외합니다."
                    : $"`{source}` 소스는 이미 제외되어 있습니다.")
                : (changed
                    ? $"내 개인 조회에 `{source}` 소스를 다시 포함합니다."
                    : $"`{source}` 소스는 차단 목록에 없습니다."),
            ephemeral: true).ConfigureAwait(false);
    }

    private async Task HandleMutedSourcesAsync(SocketSlashCommand command)
    {
        IReadOnlySet<string> muted = await _preferenceStore.GetMutedSourcesAsync(
            DiscordArticleInteractionCodec.CreateActorId(command.User.Id),
            CancellationToken.None).ConfigureAwait(false);
        await command.RespondAsync(
            muted.Count == 0
                ? "개인 차단 소스가 없습니다."
                : "개인 차단 소스: " + string.Join(", ", muted.Select(static source => $"`{source}`")),
            ephemeral: true).ConfigureAwait(false);
    }

    private static void RequireManager(SocketSlashCommand command)
    {
        if (command.User is not SocketGuildUser guildUser
            || !(guildUser.GuildPermissions.Administrator
                || guildUser.GuildPermissions.ManageGuild))
        {
            throw new UnauthorizedAccessException();
        }
    }

    private static SlashCommandProperties[] BuildCommands()
    {
        SlashCommandBuilder feedAdd = new SlashCommandBuilder()
            .WithName("feed-add")
            .WithDescription("[관리자] RSS/Atom Feed를 구독합니다.");
        feedAdd.AddOption("name", ApplicationCommandOptionType.String, "고유한 소스 이름", isRequired: true);
        feedAdd.AddOption("url", ApplicationCommandOptionType.String, "RSS/Atom URL", isRequired: true);
        feedAdd.AddOption("minutes", ApplicationCommandOptionType.Integer, "Polling 주기(1~1440분, 기본 30)", minValue: 1, maxValue: 1440);

        SlashCommandBuilder routeSet = new SlashCommandBuilder()
            .WithName("route-set")
            .WithDescription("[관리자] 주제별 뉴스 목적 채널과 배치 주기를 설정합니다.");
        routeSet.AddOption("topic", ApplicationCommandOptionType.String, "ai, game-industry, economy, markets 등", isRequired: true);
        routeSet.AddOption("channel", ApplicationCommandOptionType.Channel, "게시할 Discord 채널", isRequired: true);
        routeSet.AddOption("batch-minutes", ApplicationCommandOptionType.Integer, "모아서 게시할 시간(5~1440분, 기본 30)", minValue: 5, maxValue: 1440);
        routeSet.AddOption("min-score", ApplicationCommandOptionType.Number, "최소 점수(0~100, 기본 0)", minValue: 0, maxValue: 100);

        return
        [
            feedAdd.Build(),
            new SlashCommandBuilder().WithName("feed-list").WithDescription("[관리자] 현재 RSS/Atom Feed를 확인합니다.").Build(),
            BuildStringCommand("feed-enable", "[관리자] Feed를 활성화합니다.", "name", "소스 이름"),
            BuildStringCommand("feed-disable", "[관리자] Feed를 비활성화합니다.", "name", "소스 이름"),
            routeSet.Build(),
            new SlashCommandBuilder().WithName("route-list").WithDescription("[관리자] 주제별 뉴스 라우트를 확인합니다.").Build(),
            BuildStringCommand("route-remove", "[관리자] 런타임 뉴스 라우트를 삭제합니다.", "topic", "삭제할 주제"),
            BuildStringCommand("source-mute", "내 개인 조회에서 특정 소스를 제외합니다.", "source", "정확한 소스 이름"),
            BuildStringCommand("source-unmute", "개인 차단한 소스를 다시 표시합니다.", "source", "정확한 소스 이름"),
            new SlashCommandBuilder().WithName("source-muted").WithDescription("내가 차단한 뉴스 소스를 확인합니다.").Build()
        ];
    }

    private static SlashCommandProperties BuildStringCommand(
        string name,
        string description,
        string optionName,
        string optionDescription)
    {
        SlashCommandBuilder builder = new SlashCommandBuilder()
            .WithName(name)
            .WithDescription(description);
        builder.AddOption(
            optionName,
            ApplicationCommandOptionType.String,
            optionDescription,
            isRequired: true);
        return builder.Build();
    }

    private static SocketSlashCommandDataOption GetRequiredOption(
        SocketSlashCommand command,
        string name)
    {
        SocketSlashCommandDataOption? option = command.Data.Options
            .FirstOrDefault(value => string.Equals(value.Name, name, StringComparison.Ordinal));
        return option ?? throw new ArgumentException($"Option '{name}' is required.");
    }

    private static string GetRequiredString(SocketSlashCommand command, string name)
    {
        return GetRequiredOption(command, name).Value as string
            ?? throw new ArgumentException($"Option '{name}' must be text.");
    }

    private static int GetInteger(
        SocketSlashCommand command,
        string name,
        int defaultValue)
    {
        SocketSlashCommandDataOption? option = command.Data.Options
            .FirstOrDefault(value => string.Equals(value.Name, name, StringComparison.Ordinal));
        return option?.Value switch
        {
            long value => checked((int)value),
            int value => value,
            _ => defaultValue
        };
    }

    private static decimal GetDecimal(
        SocketSlashCommand command,
        string name,
        decimal defaultValue)
    {
        SocketSlashCommandDataOption? option = command.Data.Options
            .FirstOrDefault(value => string.Equals(value.Name, name, StringComparison.Ordinal));
        return option?.Value switch
        {
            double value => (decimal)value,
            float value => (decimal)value,
            decimal value => value,
            long value => value,
            _ => defaultValue
        };
    }

    private static SocketGuildChannel GetRequiredChannel(
        SocketSlashCommand command,
        string name)
    {
        object value = GetRequiredOption(command, name).Value;
        return value as SocketGuildChannel
            ?? throw new ArgumentException($"Option '{name}' must be a guild channel.");
    }
}
