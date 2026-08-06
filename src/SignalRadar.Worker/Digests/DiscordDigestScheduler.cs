using System.Globalization;
using SignalRadar.Application.Digests;
using SignalRadar.Bot.Discord;
using SignalRadar.Domain.Articles;

namespace SignalRadar.Worker.Digests;

public sealed record DiscordDigestScheduleOptions(
    ulong ChannelId,
    ulong ActorUserId,
    TimeZoneInfo TimeZone,
    bool DailyEnabled,
    TimeSpan DailyTime,
    bool WeeklyEnabled,
    DayOfWeek WeeklyDay,
    TimeSpan WeeklyTime,
    ArticleTopic Topic,
    int Limit,
    TimeSpan PollInterval,
    TimeSpan LeaseDuration)
{
    public bool Enabled => DailyEnabled || WeeklyEnabled;
}

public sealed class DiscordDigestScheduler
{
    private readonly DiscordDigestScheduleOptions _options;
    private readonly GenerateArticleDigestUseCase _digestUseCase;
    private readonly IDigestDeliveryReceiptStore _receiptStore;
    private readonly DiscordInboxGateway _gateway;
    private readonly TimeProvider _timeProvider;
    private readonly Action<string> _log;

    public DiscordDigestScheduler(
        DiscordDigestScheduleOptions options,
        GenerateArticleDigestUseCase digestUseCase,
        IDigestDeliveryReceiptStore receiptStore,
        DiscordInboxGateway gateway,
        TimeProvider timeProvider,
        Action<string>? log = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _digestUseCase = digestUseCase
            ?? throw new ArgumentNullException(nameof(digestUseCase));
        _receiptStore = receiptStore
            ?? throw new ArgumentNullException(nameof(receiptStore));
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        _log = log ?? (static _ => { });

        if (!_options.Enabled)
        {
            throw new ArgumentException(
                "At least one digest schedule must be enabled.",
                nameof(options));
        }

        if (_options.ChannelId == 0 || _options.ActorUserId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        if (_options.Limit is < 1 or > 10
            || _options.PollInterval < TimeSpan.FromSeconds(10)
            || _options.PollInterval > TimeSpan.FromMinutes(10)
            || _options.LeaseDuration <= TimeSpan.Zero
            || _options.LeaseDuration > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _log("Scheduled Discord digest loop started.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await TryDeliverDueDigestsAsync(cancellationToken)
                    .ConfigureAwait(false);
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
            _log("Scheduled Discord digest loop stopped.");
        }
    }

    private async ValueTask TryDeliverDueDigestsAsync(
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset localNow = TimeZoneInfo.ConvertTime(now, _options.TimeZone);

        if (_options.DailyEnabled && localNow.TimeOfDay >= _options.DailyTime)
        {
            DateTimeOffset occurrence = ConvertLocalOccurrenceToUtc(
                localNow.Date,
                _options.DailyTime);
            await TryDeliverAsync(
                ArticleDigestPeriod.Daily,
                occurrence,
                now,
                cancellationToken).ConfigureAwait(false);
        }

        if (_options.WeeklyEnabled
            && localNow.DayOfWeek == _options.WeeklyDay
            && localNow.TimeOfDay >= _options.WeeklyTime)
        {
            DateTimeOffset occurrence = ConvertLocalOccurrenceToUtc(
                localNow.Date,
                _options.WeeklyTime);
            await TryDeliverAsync(
                ArticleDigestPeriod.Weekly,
                occurrence,
                now,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask TryDeliverAsync(
        ArticleDigestPeriod period,
        DateTimeOffset occurrence,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string deliveryKey = string.Create(
            CultureInfo.InvariantCulture,
            $"discord:{period.ToString().ToLowerInvariant()}:{_options.ChannelId}:"
                + $"{_options.ActorUserId}:{(int)_options.Topic}:{_options.Limit}");
        DigestDeliveryLease? lease = await _receiptStore.TryBeginAsync(
            deliveryKey,
            occurrence,
            now,
            _options.LeaseDuration,
            cancellationToken).ConfigureAwait(false);

        if (lease is null)
        {
            return;
        }

        try
        {
            ArticleDigest digest = await _digestUseCase.GenerateAsync(
                DiscordArticleInteractionCodec.CreateActorId(_options.ActorUserId),
                period,
                _options.Topic,
                _options.Limit,
                cancellationToken).ConfigureAwait(false);
            ulong? messageId = null;

            if (digest.Articles.Count > 0)
            {
                messageId = await _gateway.SendDigestAsync(
                    _options.ChannelId,
                    digest,
                    cancellationToken).ConfigureAwait(false);
            }

            await _receiptStore.CompleteAsync(
                lease,
                _timeProvider.GetUtcNow(),
                messageId,
                cancellationToken).ConfigureAwait(false);
            _log($"Scheduled {period} digest completed with {digest.Articles.Count} articles.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await _receiptStore.FailAsync(
                lease,
                _timeProvider.GetUtcNow(),
                exception.Message,
                cancellationToken).ConfigureAwait(false);
            _log($"Scheduled {period} digest failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private DateTimeOffset ConvertLocalOccurrenceToUtc(
        DateTime localDate,
        TimeSpan localTime)
    {
        DateTime local = DateTime.SpecifyKind(
            localDate.Add(localTime),
            DateTimeKind.Unspecified);

        if (_options.TimeZone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        TimeSpan offset = _options.TimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
