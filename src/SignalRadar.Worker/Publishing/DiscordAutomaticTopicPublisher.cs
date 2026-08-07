using SignalRadar.Application.Publishing;
using SignalRadar.Bot.Discord;

namespace SignalRadar.Worker.Publishing;

public sealed class DiscordAutomaticTopicPublisher
{
    private readonly DiscordAutomaticTopicPublishingOptions _options;
    private readonly IAutomaticTopicPublicationStore _store;
    private readonly DiscordInboxGateway _gateway;
    private readonly TimeProvider _timeProvider;
    private readonly Action<string> _log;

    public DiscordAutomaticTopicPublisher(
        DiscordAutomaticTopicPublishingOptions options,
        IAutomaticTopicPublicationStore store,
        DiscordInboxGateway gateway,
        TimeProvider timeProvider,
        Action<string>? log = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        _log = log ?? (static _ => { });

        if (_options.Routes.Count == 0)
        {
            throw new ArgumentException(
                "At least one automatic topic route is required.",
                nameof(options));
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset activatedAt = await _store.GetOrCreateActivationTimeAsync(
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);
        _log(
            $"Automatic Discord topic publishing started for {_options.Routes.Count} routes "
                + $"from {activatedAt:O} with minimum score {_options.MinimumScore:0.##}.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                DateTimeOffset now = _timeProvider.GetUtcNow();

                for (int routeIndex = 0;
                    routeIndex < _options.Routes.Count;
                    routeIndex++)
                {
                    DiscordTopicPublicationRoute route = _options.Routes[routeIndex];

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
            _log("Automatic Discord topic publishing stopped.");
        }
    }

    private async ValueTask PublishRouteAsync(
        DiscordTopicPublicationRoute route,
        DateTimeOffset activatedAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        AutomaticTopicPublicationQuery query = new(
            route.Topic,
            route.ChannelId,
            activatedAt,
            _options.MinimumScore,
            _options.BatchSize,
            DiscordAutomaticTopicPublishingOptions.PublicationKind);
        IReadOnlyList<AutomaticTopicPublicationCandidate> candidates =
            await _store.GetCandidatesAsync(
                query,
                now,
                cancellationToken).ConfigureAwait(false);

        for (int index = 0; index < candidates.Count; index++)
        {
            AutomaticTopicPublicationCandidate candidate = candidates[index];
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

            try
            {
                ulong resourceId = await _gateway.SendAutomaticTopicArticleAsync(
                    route.ChannelId,
                    candidate,
                    cancellationToken).ConfigureAwait(false);
                await _store.CompleteAsync(
                    lease,
                    _timeProvider.GetUtcNow(),
                    resourceId,
                    cancellationToken).ConfigureAwait(false);
                _log(
                    $"Published article {candidate.ArticleId} to Discord channel "
                        + $"{route.ChannelId} as public resource {resourceId}.");
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException)
            {
                await _store.FailAsync(
                    lease,
                    _timeProvider.GetUtcNow(),
                    _options.RetryDelay,
                    exception.Message,
                    cancellationToken).ConfigureAwait(false);
                _log(
                    $"Automatic article publication {candidate.ArticleId} failed: "
                        + $"{exception.GetType().Name}: {exception.Message}");
            }
        }
    }
}
