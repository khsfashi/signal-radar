using SignalRadar.Application.ExternalSources;

namespace SignalRadar.Worker.ExternalSources;

public sealed class ExternalSourcePollingLoop
{
    private readonly IExternalSourceStore _sourceStore;
    private readonly CollectExternalSourceUseCase _collectSource;
    private readonly TimeProvider _timeProvider;
    private readonly int _batchSize;
    private readonly TimeSpan _leaseDuration;
    private readonly TimeSpan _idleDelay;
    private readonly Action<string> _log;

    public ExternalSourcePollingLoop(
        IExternalSourceStore sourceStore,
        CollectExternalSourceUseCase collectSource,
        TimeProvider timeProvider,
        int batchSize,
        TimeSpan leaseDuration,
        TimeSpan idleDelay,
        Action<string>? log = null)
    {
        _sourceStore = sourceStore
            ?? throw new ArgumentNullException(nameof(sourceStore));
        _collectSource = collectSource
            ?? throw new ArgumentNullException(nameof(collectSource));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));

        if (batchSize is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        if (leaseDuration < TimeSpan.FromSeconds(30)
            || leaseDuration > TimeSpan.FromMinutes(30))
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }

        if (idleDelay < TimeSpan.FromSeconds(1)
            || idleDelay > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(idleDelay));
        }

        _batchSize = batchSize;
        _leaseDuration = leaseDuration;
        _idleDelay = idleDelay;
        _log = log ?? (_ => { });
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _log("External API polling loop started.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                IReadOnlyList<ExternalSourceLease> sources = await _sourceStore
                    .ClaimDueAsync(
                        _batchSize,
                        _leaseDuration,
                        cancellationToken)
                    .ConfigureAwait(false);

                if (sources.Count == 0)
                {
                    await Task
                        .Delay(_idleDelay, _timeProvider, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                Task[] tasks = new Task[sources.Count];

                for (int index = 0; index < sources.Count; index++)
                {
                    tasks[index] = CollectAndLogAsync(
                        sources[index],
                        cancellationToken);
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _log("External API polling loop stopped.");
        }
    }

    private async Task CollectAndLogAsync(
        ExternalSourceLease source,
        CancellationToken cancellationToken)
    {
        ExternalCollectionSummary summary = await _collectSource
            .ExecuteAsync(source, cancellationToken)
            .ConfigureAwait(false);

        _log(
            $"External={summary.SourceName}, Status={summary.Status}, "
            + $"Seen={summary.ItemsSeen}, Added={summary.ArticlesAdded}, "
            + $"Duplicate={summary.ArticlesDuplicate}, Error={summary.Error}");
    }
}
