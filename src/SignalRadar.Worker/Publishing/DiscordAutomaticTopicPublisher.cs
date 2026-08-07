using SignalRadar.Application.Publishing;
using SignalRadar.Application.Translation;
using SignalRadar.Bot.Discord;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Worker.Publishing;

public sealed class DiscordAutomaticTopicPublisher
{
    private readonly DiscordAutomaticTopicPublishingOptions _options;
    private readonly IAutomaticTopicPublicationStore _store;
    private readonly IDiscordTopicRouteStore _routeStore;
    private readonly ITitleTranslator _translator;
    private readonly DiscordInboxGateway _gateway;
    private readonly TimeProvider _timeProvider;
    private readonly Action<string> _log;

    public DiscordAutomaticTopicPublisher(
        DiscordAutomaticTopicPublishingOptions options,
        IAutomaticTopicPublicationStore store,
        IDiscordTopicRouteStore routeStore,
        ITitleTranslator translator,
        DiscordInboxGateway gateway,
        TimeProvider timeProvider,
        Action<string>? log = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _routeStore = routeStore ?? throw new ArgumentNullException(nameof(routeStore));
        _translator = translator ?? throw new ArgumentNullException(nameof(translator));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        _log = log ?? (static _ => { });
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset activatedAt = await _store.GetOrCreateActivationTimeAsync(
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        _log(
            $"Batched Discord topic publishing started from {activatedAt:O}; "
                + "runtime routes can be managed from Discord.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                DateTimeOffset now = _timeProvider.GetUtcNow();
                IReadOnlyList<ManagedDiscordTopicRoute> routes =
                    await GetEffectiveRoutesAsync(cancellationToken).ConfigureAwait(false);

                for (int routeIndex = 0; routeIndex < routes.Count; routeIndex++)
                {
                    ManagedDiscordTopicRoute route = routes[routeIndex];

                    try
                    {
                        await PublishRouteAsync(
                            route,
                            activatedAt,
                            now,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                        when (exception is not OperationCanceledException)
                    {
                        _log(
                            $"Automatic topic route {route.Topic} -> {route.ChannelId} failed: "
                                + $"{exception.GetType().Name}: {exception.Message}");
                    }
                }

                await Task.Delay(
                    _options.PollInterval,
                    _timeProvider,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _log("Batched Discord topic publishing stopped.");
        }
    }

    private async ValueTask<IReadOnlyList<ManagedDiscordTopicRoute>> GetEffectiveRoutesAsync(
        CancellationToken cancellationToken)
    {
        Dictionary<ArticleTopic, ManagedDiscordTopicRoute> routes = [];

        for (int index = 0; index < _options.Routes.Count; index++)
        {
            DiscordTopicPublicationRoute route = _options.Routes[index];
            routes[route.Topic] = new ManagedDiscordTopicRoute(
                route.Topic,
                route.ChannelId,
                route.MinimumScore,
                route.BatchWindow);
        }

        IReadOnlyList<ManagedDiscordTopicRoute> managed = await _routeStore
            .GetAllAsync(cancellationToken).ConfigureAwait(false);

        for (int index = 0; index < managed.Count; index++)
        {
            ManagedDiscordTopicRoute route = managed[index];

            if (route.Enabled)
            {
                routes[route.Topic] = route;
            }
            else
            {
                routes.Remove(route.Topic);
            }
        }

        return [.. routes.Values.OrderBy(static route => (int)route.Topic)];
    }

    private async ValueTask PublishRouteAsync(
        ManagedDiscordTopicRoute route,
        DateTimeOffset activatedAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        AutomaticTopicPublicationQuery query = new(
            route.Topic,
            route.ChannelId,
            activatedAt,
            route.MinimumScore,
            _options.BatchSize,
            DiscordAutomaticTopicPublishingOptions.PublicationKind);
        IReadOnlyList<AutomaticTopicPublicationCandidate> candidates =
            await _store.GetCandidatesAsync(
                query,
                now,
                cancellationToken).ConfigureAwait(false);
        DateTimeOffset cutoff = now - route.BatchWindow;
        List<AutomaticTopicPublicationLease> leases = new(candidates.Count);
        List<AutomaticTopicPublicationCandidate> batch = new(candidates.Count);

        for (int index = 0; index < candidates.Count; index++)
        {
            AutomaticTopicPublicationCandidate candidate = candidates[index];

            if (candidate.CollectedAt > cutoff)
            {
                break;
            }

            AutomaticTopicPublicationLease? lease = await _store.TryBeginAsync(
                candidate.ArticleId,
                route.ChannelId,
                DiscordAutomaticTopicPublishingOptions.PublicationKind,
                _timeProvider.GetUtcNow(),
                _options.LeaseDuration,
                cancellationToken).ConfigureAwait(false);

            if (lease is null)
            {
                continue;
            }

            string translatedTitle = await _translator.TranslateAsync(
                candidate.Title,
                cancellationToken).ConfigureAwait(false);
            leases.Add(lease);
            batch.Add(candidate with { Title = translatedTitle });
        }

        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            ulong resourceId = await _gateway.SendAutomaticTopicBatchAsync(
                route.ChannelId,
                route.Topic,
                batch,
                route.BatchWindow,
                now,
                cancellationToken).ConfigureAwait(false);
            DateTimeOffset deliveredAt = _timeProvider.GetUtcNow();

            for (int index = 0; index < leases.Count; index++)
            {
                await _store.CompleteAsync(
                    leases[index],
                    deliveredAt,
                    resourceId,
                    cancellationToken).ConfigureAwait(false);
            }

            _log(
                $"Published {batch.Count} {route.Topic} articles to Discord channel "
                    + $"{route.ChannelId} as batch resource {resourceId}.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DateTimeOffset failedAt = _timeProvider.GetUtcNow();

            for (int index = 0; index < leases.Count; index++)
            {
                await _store.FailAsync(
                    leases[index],
                    failedAt,
                    _options.RetryDelay,
                    exception.Message,
                    CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }
    }
}
