using Npgsql;
using SignalRadar.Application.Articles;
using SignalRadar.Application.Digests;
using SignalRadar.Application.Translation;
using SignalRadar.Bot.Discord;
using SignalRadar.Domain.Articles;
using SignalRadar.Infrastructure.Articles;
using SignalRadar.Infrastructure.Digests;
using SignalRadar.Infrastructure.Discord;
using SignalRadar.Infrastructure.Feeds;
using SignalRadar.Infrastructure.Operations;
using SignalRadar.Infrastructure.Preferences;
using SignalRadar.Infrastructure.Publishing;
using SignalRadar.Infrastructure.Ranking;
using SignalRadar.Infrastructure.Translation;
using SignalRadar.Worker.Digests;
using SignalRadar.Worker.Publishing;
using SignalRadar.Worker.Summaries;

namespace SignalRadar.Worker.Runtime;

internal sealed class DiscordRuntime : IDisposable
{
    private readonly DiscordInboxGateway _gateway;
    private readonly HttpClient? _translationHttpClient;

    private DiscordRuntime(
        DiscordInboxGateway gateway,
        HttpClient? translationHttpClient,
        IReadOnlyList<Task> tasks)
    {
        _gateway = gateway;
        _translationHttpClient = translationHttpClient;
        Tasks = tasks;
    }

    public IReadOnlyList<Task> Tasks { get; }

    public static DiscordRuntime Start(
        NpgsqlDataSource dataSource,
        CollectArticleUseCase collectArticle,
        IArticleAssessmentPolicy assessmentPolicy,
        string rankingProfileVersion,
        TimeProvider timeProvider,
        DateTimeOffset startedAt,
        SummaryRuntime summaryRuntime,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        ulong[] allowedGuildIds = WorkerEnvironment.ParseSnowflakes(
            "DISCORD_ALLOWED_GUILD_IDS",
            required: true);
        ulong[] allowedChannelIds = WorkerEnvironment.ParseSnowflakes(
            "DISCORD_ALLOWED_CHANNEL_IDS",
            required: true);
        DiscordDigestScheduleOptions? digestSchedule =
            DiscordDigestScheduleConfiguration.Load(
                discordEnabled: true,
                allowedChannelIds);
        DiscordAutomaticTopicPublishingOptions? topicPublishing =
            DiscordAutomaticTopicPublishingConfiguration.Load(
                discordEnabled: true,
                allowedChannelIds);

        HttpClient? translationHttpClient = null;
        ITitleTranslator titleTranslator = new PassthroughTitleTranslator();

        try
        {
            if (WorkerEnvironment.ParseBoolean(
                    "TITLE_TRANSLATION_ENABLED",
                    defaultValue: true))
            {
                string endpointValue = Environment.GetEnvironmentVariable(
                    "TITLE_TRANSLATION_ENDPOINT") ?? "http://libretranslate:5000/";

                if (!Uri.TryCreate(
                        endpointValue,
                        UriKind.Absolute,
                        out Uri? translationEndpoint)
                    || translationEndpoint.Scheme is not ("http" or "https"))
                {
                    throw new InvalidOperationException(
                        "TITLE_TRANSLATION_ENDPOINT must be an absolute HTTP(S) URL.");
                }

                SocketsHttpHandler translationHandler = WorkerHttp.CreateHandler(
                    WorkerEnvironment.ParseInteger(
                        "TITLE_TRANSLATION_HTTP_MAX_CONNECTIONS_PER_SERVER",
                        defaultValue: 2,
                        minimum: 1,
                        maximum: 8));
                translationHttpClient = new HttpClient(
                    translationHandler,
                    disposeHandler: true)
                {
                    Timeout = TimeSpan.FromSeconds(WorkerEnvironment.ParseInteger(
                        "TITLE_TRANSLATION_TIMEOUT_SECONDS",
                        defaultValue: 10,
                        minimum: 1,
                        maximum: 60))
                };
                titleTranslator = new PostgresLibreTranslateTitleTranslator(
                    dataSource,
                    translationHttpClient,
                    translationEndpoint,
                    log);
                log(
                    $"Title translation enabled through {translationEndpoint.Host} "
                        + "with PostgreSQL cache.");
            }

            DiscordInboxOptions discordOptions = new(
                WorkerEnvironment.GetRequired("DISCORD_BOT_TOKEN"),
                allowedGuildIds,
                allowedChannelIds,
                WorkerEnvironment.ParseSnowflakes(
                    "DISCORD_ALLOWED_AUTHOR_IDS",
                    required: false),
                WorkerEnvironment.ParseBoolean(
                    "DISCORD_REQUIRE_AUTOMATED_AUTHOR",
                    defaultValue: true),
                Environment.GetEnvironmentVariable("DISCORD_SOURCE_NAME")
                    ?? "discord");
            DiscordMessageAccessPolicy accessPolicy = new(discordOptions);
            DiscordMessageArticleCandidateFactory candidateFactory = new();
            PostgresDiscordMessageReceiptStore receiptStore = new(
                dataSource,
                TimeSpan.FromMinutes(5));
            DiscordInboxProcessor processor = new(
                discordOptions,
                accessPolicy,
                candidateFactory,
                receiptStore,
                collectArticle);
            DiscordSocketMessageMapper mapper = new();
            PostgresArticleRankingReader baseRankingReader = new(dataSource);
            PostgresSourcePreferenceStore sourcePreferenceStore = new(dataSource);
            IArticleRankingReader rankingReader = new TranslatingArticleRankingReader(
                new SourceFilteredArticleRankingReader(
                    baseRankingReader,
                    sourcePreferenceStore),
                titleTranslator);
            PostgresArticleFeedbackStore feedbackStore = new(dataSource);
            PostgresArticleSaveStore saveStore = new(dataSource);
            IArticleSavedReader savedReader = new TranslatingArticleSavedReader(
                saveStore,
                titleTranslator);
            SavedArticleMarkdownExporter markdownExporter = new();
            GenerateArticleDigestUseCase digestUseCase = new(
                rankingReader,
                timeProvider);
            PostgresSignalRadarStatusReader statusReader = new(
                dataSource,
                timeProvider,
                startedAt,
                rankingProfileVersion,
                summaryRuntime.Description,
                digestSchedule is not null);
            DiscordArticleInteractionService interactionService = new(
                rankingReader,
                feedbackStore,
                saveStore,
                savedReader,
                markdownExporter,
                timeProvider,
                summaryRuntime.UseCase);
            DiscordArticleInteractionHandler interactionHandler = new(
                discordOptions,
                interactionService,
                log,
                digestUseCase,
                statusReader);
            DiscordHelpCommandHandler helpCommandHandler = new(
                discordOptions,
                summaryRuntime.Enabled,
                digestEnabled: true,
                statusEnabled: true,
                automaticTopicPublishingEnabled: topicPublishing is not null,
                log);
            PostgresFeedSourceAdministrationStore feedAdministrationStore = new(
                dataSource);
            PostgresDiscordTopicRouteStore topicRouteStore = new(dataSource);
            PostgresArticleReclassificationService reclassificationService = new(
                dataSource,
                assessmentPolicy,
                timeProvider);
            DiscordManagementCommandHandler managementCommandHandler = new(
                discordOptions,
                feedAdministrationStore,
                topicRouteStore,
                sourcePreferenceStore,
                reclassificationService,
                log);

            DiscordInboxGateway gateway = new(
                discordOptions,
                processor,
                mapper,
                log,
                interactionHandler,
                helpCommandHandler,
                managementCommandHandler);
            List<Task> tasks =
            [
                gateway.RunAsync(cancellationToken)
            ];

            if (topicPublishing is not null)
            {
                PostgresAutomaticTopicPublicationStore publicationStore = new(
                    dataSource);
                DiscordAutomaticTopicPublisher publisher = new(
                    topicPublishing,
                    publicationStore,
                    topicRouteStore,
                    titleTranslator,
                    gateway,
                    timeProvider,
                    log);
                tasks.Add(publisher.RunAsync(cancellationToken));
                log(
                    $"Batched automatic topic publishing enabled; "
                        + $"{topicPublishing.Routes.Count} legacy environment routes loaded, "
                        + "runtime routes are stored in PostgreSQL.");
            }

            if (digestSchedule is not null)
            {
                PostgresDigestDeliveryReceiptStore digestReceiptStore = new(dataSource);
                DiscordDigestScheduler scheduler = new(
                    digestSchedule,
                    digestUseCase,
                    digestReceiptStore,
                    gateway,
                    timeProvider,
                    log);
                tasks.Add(scheduler.RunAsync(cancellationToken));
                log(
                    $"Scheduled digest enabled for channel {digestSchedule.ChannelId} "
                        + $"in {digestSchedule.TimeZone.Id}.");
            }

            return new DiscordRuntime(gateway, translationHttpClient, tasks);
        }
        catch
        {
            translationHttpClient?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _gateway.Dispose();
        _translationHttpClient?.Dispose();
    }
}
