using System.Globalization;
using System.Text;
using Discord;
using Discord.WebSocket;
using SignalRadar.Application.Articles;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Bot.Discord;

public sealed class DiscordArticleInteractionHandler
{
    private const int DefaultTopDays = 3;
    private const int DefaultSearchDays = 30;
    private const int DefaultSavedDays = 365;
    private const int DefaultExportDays = 365;
    private const int DefaultDisplayLimit = 5;
    private const int DefaultExportLimit = 100;
    private readonly DiscordInboxOptions _options;
    private readonly DiscordArticleInteractionService _service;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _registrationLock = new(1, 1);
    private bool _commandsRegistered;

    public DiscordArticleInteractionHandler(
        DiscordInboxOptions options,
        DiscordArticleInteractionService service,
        Action<string>? log = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _log = log ?? (static _ => { });
    }

    public async Task RegisterCommandsAsync(DiscordSocketClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        await _registrationLock.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_commandsRegistered)
            {
                return;
            }

            ApplicationCommandProperties[] commands =
            [
                BuildTopCommand(),
                BuildSearchCommand(),
                BuildSavedCommand(),
                BuildExportCommand()
            ];

            foreach (ulong guildId in _options.AllowedGuildIds)
            {
                SocketGuild? guild = client.GetGuild(guildId);

                if (guild is null)
                {
                    _log($"Discord command guild {guildId} is not available.");
                    continue;
                }

                await guild
                    .BulkOverwriteApplicationCommandAsync(commands)
                    .ConfigureAwait(false);
                _log($"Discord commands synchronized for guild {guildId}.");
            }

            _commandsRegistered = true;
        }
        finally
        {
            _registrationLock.Release();
        }
    }

    public async Task HandleSlashCommandAsync(SocketSlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!IsAllowed(command.GuildId, command.ChannelId))
        {
            await command.RespondAsync(
                "이 명령은 Signal Radar 허용 채널에서만 사용할 수 있습니다.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        try
        {
            switch (command.Data.Name)
            {
                case "top":
                    await HandleTopAsync(command).ConfigureAwait(false);
                    break;
                case "search":
                    await HandleSearchAsync(command).ConfigureAwait(false);
                    break;
                case "saved":
                    await HandleSavedAsync(command).ConfigureAwait(false);
                    break;
                case "export":
                    await HandleExportAsync(command).ConfigureAwait(false);
                    break;
                default:
                    await command.RespondAsync(
                        "지원하지 않는 Signal Radar 명령입니다.",
                        ephemeral: true).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception exception)
        {
            _log($"Discord slash command {command.Data.Name} failed: {exception}");

            if (!command.HasResponded)
            {
                await command.RespondAsync(
                    "명령을 처리하지 못했습니다. 입력값과 Worker 로그를 확인해주세요.",
                    ephemeral: true).ConfigureAwait(false);
            }
        }
    }

    public async Task HandleButtonAsync(SocketMessageComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (DiscordArticleInteractionCodec.TryParseFeedbackCustomId(
                component.Data.CustomId,
                out Guid feedbackArticleId,
                out ArticleFeedbackKind feedbackKind))
        {
            await HandleFeedbackButtonAsync(
                component,
                feedbackArticleId,
                feedbackKind).ConfigureAwait(false);
            return;
        }

        if (DiscordArticleInteractionCodec.TryParseSaveCustomId(
                component.Data.CustomId,
                out Guid savedArticleId,
                out DiscordArticleSaveAction saveAction))
        {
            await HandleSaveButtonAsync(
                component,
                savedArticleId,
                saveAction).ConfigureAwait(false);
        }
    }

    private async Task HandleFeedbackButtonAsync(
        SocketMessageComponent component,
        Guid articleId,
        ArticleFeedbackKind kind)
    {
        if (!IsAllowed(component.GuildId, component.ChannelId))
        {
            await component.RespondAsync(
                "이 버튼은 Signal Radar 허용 채널에서만 사용할 수 있습니다.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        try
        {
            await _service.SetFeedbackAsync(
                component.User.Id,
                articleId,
                kind,
                CancellationToken.None).ConfigureAwait(false);
            await component.RespondAsync(
                GetFeedbackConfirmation(kind),
                ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _log($"Discord feedback for article {articleId} failed: {exception}");

            if (!component.HasResponded)
            {
                await component.RespondAsync(
                    "피드백을 저장하지 못했습니다.",
                    ephemeral: true).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleSaveButtonAsync(
        SocketMessageComponent component,
        Guid articleId,
        DiscordArticleSaveAction action)
    {
        if (!IsAllowed(component.GuildId, component.ChannelId))
        {
            await component.RespondAsync(
                "이 버튼은 Signal Radar 허용 채널에서만 사용할 수 있습니다.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        try
        {
            bool changed = action switch
            {
                DiscordArticleSaveAction.Add => await _service.SaveAsync(
                    component.User.Id,
                    articleId,
                    CancellationToken.None).ConfigureAwait(false),
                DiscordArticleSaveAction.Remove => await _service.RemoveSavedAsync(
                    component.User.Id,
                    articleId,
                    CancellationToken.None).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(action))
            };
            await component.RespondAsync(
                GetSaveConfirmation(action, changed),
                ephemeral: true).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _log($"Discord save action for article {articleId} failed: {exception}");

            if (!component.HasResponded)
            {
                await component.RespondAsync(
                    "기사 저장 상태를 변경하지 못했습니다.",
                    ephemeral: true).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleTopAsync(SocketSlashCommand command)
    {
        int days = GetIntegerOption(command, "days", DefaultTopDays);
        int limit = GetIntegerOption(command, "limit", DefaultDisplayLimit);
        ArticleTopic topic = DiscordArticleInteractionCodec.ParseTopic(
            GetStringOption(command, "topic"));
        IReadOnlyList<RankedArticle> articles = await _service.GetTopAsync(
            command.User.Id,
            days,
            topic,
            limit,
            CancellationToken.None).ConfigureAwait(false);

        await RespondWithRankedArticlesAsync(
            command,
            articles,
            $"최근 {days}일 · {DiscordArticleInteractionCodec.GetTopicLabel(topic)}")
            .ConfigureAwait(false);
    }

    private async Task HandleSearchAsync(SocketSlashCommand command)
    {
        string searchText = GetRequiredStringOption(command, "query");
        int days = GetIntegerOption(command, "days", DefaultSearchDays);
        int limit = GetIntegerOption(command, "limit", DefaultDisplayLimit);
        ArticleTopic topic = DiscordArticleInteractionCodec.ParseTopic(
            GetStringOption(command, "topic"));
        IReadOnlyList<RankedArticle> articles = await _service.SearchAsync(
            command.User.Id,
            searchText,
            days,
            topic,
            limit,
            CancellationToken.None).ConfigureAwait(false);

        await RespondWithRankedArticlesAsync(
            command,
            articles,
            $"검색: {searchText} · 최근 {days}일 · {DiscordArticleInteractionCodec.GetTopicLabel(topic)}")
            .ConfigureAwait(false);
    }

    private async Task HandleSavedAsync(SocketSlashCommand command)
    {
        int days = GetIntegerOption(command, "days", DefaultSavedDays);
        int limit = GetIntegerOption(command, "limit", DefaultDisplayLimit);
        ArticleTopic topic = DiscordArticleInteractionCodec.ParseTopic(
            GetStringOption(command, "topic"));
        IReadOnlyList<SavedArticle> articles = await _service.GetSavedAsync(
            command.User.Id,
            days,
            topic,
            limit,
            CancellationToken.None).ConfigureAwait(false);

        await RespondWithSavedArticlesAsync(
            command,
            articles,
            $"최근 {days}일 내 저장 · {DiscordArticleInteractionCodec.GetTopicLabel(topic)}")
            .ConfigureAwait(false);
    }

    private async Task HandleExportAsync(SocketSlashCommand command)
    {
        int days = GetIntegerOption(command, "days", DefaultExportDays);
        int limit = GetIntegerOption(command, "limit", DefaultExportLimit);
        ArticleTopic topic = DiscordArticleInteractionCodec.ParseTopic(
            GetStringOption(command, "topic"));
        ArticleMarkdownExport export = await _service.ExportSavedAsync(
            command.User.Id,
            days,
            topic,
            limit,
            CancellationToken.None).ConfigureAwait(false);

        if (export.ArticleCount == 0)
        {
            await command.RespondAsync(
                "내보낼 저장 기사가 없습니다.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(export.Content);
        using MemoryStream stream = new(bytes, writable: false);
        await command.RespondWithFileAsync(
            stream,
            export.FileName,
            text: $"저장 기사 {export.ArticleCount}건을 Markdown으로 내보냈습니다.",
            ephemeral: true,
            allowedMentions: AllowedMentions.None).ConfigureAwait(false);
    }

    private static async Task RespondWithRankedArticlesAsync(
        SocketSlashCommand command,
        IReadOnlyList<RankedArticle> articles,
        string heading)
    {
        if (articles.Count == 0)
        {
            await command.RespondAsync(
                $"{heading}\n조건에 맞는 기사가 없습니다.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        Embed[] embeds = new Embed[articles.Count];
        ComponentBuilder components = new();

        for (int index = 0; index < articles.Count; index++)
        {
            RankedArticle article = articles[index];
            int displayIndex = index + 1;
            embeds[index] = BuildRankedArticleEmbed(article, displayIndex);
            AddArticleButtons(
                components,
                article.ArticleId,
                displayIndex,
                index,
                DiscordArticleSaveAction.Add);
        }

        await command.RespondAsync(
            $"{heading} · {articles.Count}건",
            embeds: embeds,
            ephemeral: true,
            allowedMentions: AllowedMentions.None,
            components: components.Build()).ConfigureAwait(false);
    }

    private static async Task RespondWithSavedArticlesAsync(
        SocketSlashCommand command,
        IReadOnlyList<SavedArticle> articles,
        string heading)
    {
        if (articles.Count == 0)
        {
            await command.RespondAsync(
                $"{heading}\n저장한 기사가 없습니다.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        Embed[] embeds = new Embed[articles.Count];
        ComponentBuilder components = new();

        for (int index = 0; index < articles.Count; index++)
        {
            SavedArticle article = articles[index];
            int displayIndex = index + 1;
            embeds[index] = BuildSavedArticleEmbed(article, displayIndex);
            AddArticleButtons(
                components,
                article.ArticleId,
                displayIndex,
                index,
                DiscordArticleSaveAction.Remove);
        }

        await command.RespondAsync(
            $"{heading} · {articles.Count}건",
            embeds: embeds,
            ephemeral: true,
            allowedMentions: AllowedMentions.None,
            components: components.Build()).ConfigureAwait(false);
    }

    private static Embed BuildRankedArticleEmbed(
        RankedArticle article,
        int displayIndex)
    {
        string description = string.Create(
            CultureInfo.InvariantCulture,
            $"출처: {article.Source}\n점수: {article.EffectiveScore:0.00} "
                + $"(기본 {article.BaseScore:0.00}, 피드백 {article.FeedbackWeight:+#;-#;0})\n"
                + $"주제: {DiscordArticleInteractionCodec.FormatTopics(article.Topics)}");

        return new EmbedBuilder()
            .WithTitle(Truncate(article.Title, EmbedBuilder.MaxTitleLength))
            .WithUrl(article.CanonicalUrl.AbsoluteUri)
            .WithDescription(description)
            .WithFooter($"#{displayIndex} · 대표 주제: "
                + DiscordArticleInteractionCodec.GetTopicLabel(article.PrimaryTopic))
            .WithTimestamp(article.PublishedAt)
            .Build();
    }

    private static Embed BuildSavedArticleEmbed(
        SavedArticle article,
        int displayIndex)
    {
        string description = string.Create(
            CultureInfo.InvariantCulture,
            $"출처: {article.Source}\n점수: {article.EffectiveScore:0.00} "
                + $"(기본 {article.BaseScore:0.00}, 피드백 {article.FeedbackWeight:+#;-#;0})\n"
                + $"저장: {article.SavedAt.ToUniversalTime():yyyy-MM-dd HH:mm} UTC\n"
                + $"주제: {DiscordArticleInteractionCodec.FormatTopics(article.Topics)}");

        return new EmbedBuilder()
            .WithTitle(Truncate(article.Title, EmbedBuilder.MaxTitleLength))
            .WithUrl(article.CanonicalUrl.AbsoluteUri)
            .WithDescription(description)
            .WithFooter($"#{displayIndex} · 대표 주제: "
                + DiscordArticleInteractionCodec.GetTopicLabel(article.PrimaryTopic))
            .WithTimestamp(article.PublishedAt)
            .Build();
    }

    private static void AddArticleButtons(
        ComponentBuilder builder,
        Guid articleId,
        int displayIndex,
        int row,
        DiscordArticleSaveAction saveAction)
    {
        builder.WithButton(
            $"{displayIndex} 관심",
            DiscordArticleInteractionCodec.CreateFeedbackCustomId(
                ArticleFeedbackKind.Interested,
                articleId),
            ButtonStyle.Success,
            row: row);
        builder.WithButton(
            $"{displayIndex} 별로",
            DiscordArticleInteractionCodec.CreateFeedbackCustomId(
                ArticleFeedbackKind.NotInterested,
                articleId),
            ButtonStyle.Secondary,
            row: row);
        builder.WithButton(
            saveAction == DiscordArticleSaveAction.Add
                ? $"{displayIndex} 저장"
                : $"{displayIndex} 저장 해제",
            DiscordArticleInteractionCodec.CreateSaveCustomId(
                saveAction,
                articleId),
            ButtonStyle.Primary,
            row: row);
        builder.WithButton(
            $"{displayIndex} 숨김",
            DiscordArticleInteractionCodec.CreateFeedbackCustomId(
                ArticleFeedbackKind.Hidden,
                articleId),
            ButtonStyle.Danger,
            row: row);
    }

    private bool IsAllowed(ulong? guildId, ulong? channelId)
    {
        return guildId.HasValue
            && channelId.HasValue
            && _options.AllowedGuildIds.Contains(guildId.Value)
            && _options.AllowedChannelIds.Contains(channelId.Value);
    }

    private static SlashCommandProperties BuildTopCommand()
    {
        SlashCommandBuilder builder = new SlashCommandBuilder()
            .WithName("top")
            .WithDescription("점수가 높은 최신 기술 뉴스를 조회합니다.");
        builder.AddOption(
            "days",
            ApplicationCommandOptionType.Integer,
            "조회 기간(1~30일, 기본 3일)",
            minValue: 1,
            maxValue: 30);
        AddTopicOption(builder);
        builder.AddOption(
            "limit",
            ApplicationCommandOptionType.Integer,
            "결과 개수(1~5, 기본 5개)",
            minValue: 1,
            maxValue: 5);
        return builder.Build();
    }

    private static SlashCommandProperties BuildSearchCommand()
    {
        SlashCommandBuilder builder = new SlashCommandBuilder()
            .WithName("search")
            .WithDescription("수집된 기사 제목과 출처를 검색합니다.");
        builder.AddOption(
            "query",
            ApplicationCommandOptionType.String,
            "검색어(2~100자)",
            isRequired: true,
            minLength: 2,
            maxLength: 100);
        builder.AddOption(
            "days",
            ApplicationCommandOptionType.Integer,
            "조회 기간(1~30일, 기본 30일)",
            minValue: 1,
            maxValue: 30);
        AddTopicOption(builder);
        builder.AddOption(
            "limit",
            ApplicationCommandOptionType.Integer,
            "결과 개수(1~5, 기본 5개)",
            minValue: 1,
            maxValue: 5);
        return builder.Build();
    }

    private static SlashCommandProperties BuildSavedCommand()
    {
        SlashCommandBuilder builder = new SlashCommandBuilder()
            .WithName("saved")
            .WithDescription("내가 저장한 기사를 최근 저장 순으로 조회합니다.");
        builder.AddOption(
            "days",
            ApplicationCommandOptionType.Integer,
            "저장 기간(1~3650일, 기본 365일)",
            minValue: 1,
            maxValue: 3650);
        AddTopicOption(builder);
        builder.AddOption(
            "limit",
            ApplicationCommandOptionType.Integer,
            "결과 개수(1~5, 기본 5개)",
            minValue: 1,
            maxValue: 5);
        return builder.Build();
    }

    private static SlashCommandProperties BuildExportCommand()
    {
        SlashCommandBuilder builder = new SlashCommandBuilder()
            .WithName("export")
            .WithDescription("내가 저장한 기사 링크를 Markdown 파일로 내보냅니다.");
        builder.AddOption(
            "days",
            ApplicationCommandOptionType.Integer,
            "저장 기간(1~3650일, 기본 365일)",
            minValue: 1,
            maxValue: 3650);
        AddTopicOption(builder);
        builder.AddOption(
            "limit",
            ApplicationCommandOptionType.Integer,
            "내보낼 개수(1~100, 기본 100개)",
            minValue: 1,
            maxValue: 100);
        return builder.Build();
    }

    private static void AddTopicOption(SlashCommandBuilder builder)
    {
        builder.AddOption(
            "topic",
            ApplicationCommandOptionType.String,
            "all, ai, game-industry, game-development, developer-tools, research, business, security");
    }

    private static int GetIntegerOption(
        SocketSlashCommand command,
        string name,
        int defaultValue)
    {
        SocketSlashCommandDataOption? option = command.Data.Options
            .FirstOrDefault(candidate => string.Equals(
                candidate.Name,
                name,
                StringComparison.Ordinal));

        return option?.Value switch
        {
            long value => checked((int)value),
            int value => value,
            double value => checked((int)value),
            null => defaultValue,
            _ => throw new InvalidOperationException(
                $"Discord option '{name}' is not an integer.")
        };
    }

    private static string? GetStringOption(
        SocketSlashCommand command,
        string name)
    {
        SocketSlashCommandDataOption? option = command.Data.Options
            .FirstOrDefault(candidate => string.Equals(
                candidate.Name,
                name,
                StringComparison.Ordinal));

        return option?.Value as string;
    }

    private static string GetRequiredStringOption(
        SocketSlashCommand command,
        string name)
    {
        string? value = GetStringOption(command, name);

        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"Required Discord option '{name}' is missing.");
    }

    private static string GetFeedbackConfirmation(ArticleFeedbackKind kind)
    {
        return kind switch
        {
            ArticleFeedbackKind.Interested => "관심 있음으로 저장했습니다.",
            ArticleFeedbackKind.NotInterested => "관심 없음으로 저장했습니다.",
            ArticleFeedbackKind.Hidden => "숨김 처리했습니다. 이후 내 조회 결과에서 제외됩니다.",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static string GetSaveConfirmation(
        DiscordArticleSaveAction action,
        bool changed)
    {
        return (action, changed) switch
        {
            (DiscordArticleSaveAction.Add, true) => "기사 보관함에 저장했습니다.",
            (DiscordArticleSaveAction.Add, false) => "이미 보관함에 저장된 기사입니다.",
            (DiscordArticleSaveAction.Remove, true) => "보관함에서 제거했습니다.",
            (DiscordArticleSaveAction.Remove, false) => "이미 보관함에 없는 기사입니다.",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
    }

    private static string Truncate(string value, int maximumLength)
    {
        return value.Length <= maximumLength
            ? value
            : string.Concat(value.AsSpan(0, maximumLength - 1), "…");
    }
}
