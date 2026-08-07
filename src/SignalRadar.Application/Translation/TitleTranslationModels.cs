namespace SignalRadar.Application.Translation;

public interface ITitleTranslator
{
    public ValueTask<string> TranslateAsync(
        string title,
        CancellationToken cancellationToken);
}

public sealed class PassthroughTitleTranslator : ITitleTranslator
{
    public ValueTask<string> TranslateAsync(
        string title,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(title);
    }
}
