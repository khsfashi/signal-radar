using Discord;
using Discord.WebSocket;

namespace SignalRadar.Bot.Discord;

public sealed class DiscordHelpCommandHandler
{
    private readonly DiscordInboxOptions _options;
    private readonly bool _summaryEnabled;
    private readonly bool _digestEnabled;
    private readonly bool _statusEnabled;
    private readonly bool _automaticTopicPublishingEnabled;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _registrationLock = new(1, 1);
    private bool _registered;

    public DiscordHelpCommandHandler(
        DiscordInboxOptions options,
        bool summaryEnabled,
        bool digestEnabled,
        bool statusEnabled,
        bool automaticTopicPublishingEnabled,
        Action<string>? log = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _summaryEnabled = summaryEnabled;
        _digestEnabled = digestEnabled;
        _statusEnabled = statusEnabled;
        _automaticTopicPublishingEnabled = automaticTopicPublishingEnabled;
        _log = log ?? (static _ => { });
    }

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

            SlashCommandProperties command = new SlashCommandBuilder()
                .WithName("help")
                .WithDescription("Signal Radar 명령과 자동 공유 동작을 안내합니다.")
                .Build();

            foreach (ulong guildId in _options.AllowedGuildIds)
            {
                SocketGuild? guild = client.GetGuild(guildId);

                if (guild is null)
                {
                    _log($"Discord help command guild {guildId} is not available.");
                    continue;
                }

                await guild.CreateApplicationCommandAsync(command)
                    .ConfigureAwait(false);
            }

            _registered = true;
            _log("Discord /help command synchronized.");
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
            || !command.ChannelId.HasValue
            || !_options.AllowedGuildIds.Contains(command.GuildId.Value)
            || !_options.AllowedChannelIds.Contains(command.ChannelId.Value))
        {
            await command.RespondAsync(
                "이 명령은 Signal Radar 허용 채널에서만 사용할 수 있습니다.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        EmbedBuilder builder = new EmbedBuilder()
            .WithTitle("Signal Radar 도움말")
            .WithDescription(
                "슬래시 명령 결과는 요청한 사용자에게만 보입니다. "
                    + "자동 토픽 공유와 예약 다이제스트는 채널에 남는 공개 게시물입니다.")
            .AddField(
                "조회",
                "`/top` 최신 상위 기사\n"
                    + "`/search` 제목·출처 검색\n"
                    + "`/saved` 저장 기사 조회",
                inline: false)
            .AddField(
                "보관·내보내기",
                "`/export` 저장 기사를 Markdown으로 내보내기\n"
                    + "기사 버튼: 관심 / 별로 / 저장 / 숨김",
                inline: false);

        List<string> optionalCommands = [];

        if (_summaryEnabled)
        {
            optionalCommands.Add("`/summarize` 저장 기사 AI 브리핑");
        }

        if (_digestEnabled)
        {
            optionalCommands.Add("`/digest` 일간·주간 다이제스트 조회");
        }

        if (_statusEnabled)
        {
            optionalCommands.Add("`/status` 수집기와 DB 상태 확인");
        }

        if (optionalCommands.Count > 0)
        {
            builder.AddField(
                "추가 명령",
                string.Join("\n", optionalCommands),
                inline: false);
        }

        builder.AddField(
            "자동 공유",
            _automaticTopicPublishingEnabled
                ? "활성화됨 · 점수 기준을 통과한 새 기사가 주제별 채널에 공개됩니다."
                : "비활성화됨 · `DISCORD_TOPIC_PUBLISHING_ENABLED=true`로 켤 수 있습니다.",
            inline: false);

        await command.RespondAsync(
            embed: builder.Build(),
            ephemeral: true,
            allowedMentions: AllowedMentions.None).ConfigureAwait(false);
    }
}
