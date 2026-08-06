namespace SignalRadar.Infrastructure.Summaries;

public sealed class ArticleContentHttpOptions
{
    public ArticleContentHttpOptions(
        TimeSpan requestTimeout,
        int maximumResponseBytes,
        int maximumRobotsBytes,
        int minimumExtractedCharacters,
        int maximumExtractedCharacters,
        int maximumRedirects,
        bool allowPrivateNetworkTargets,
        TimeSpan successCacheDuration,
        TimeSpan unavailableCacheDuration,
        TimeSpan robotsCacheDuration)
    {
        if (requestTimeout < TimeSpan.FromSeconds(1)
            || requestTimeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        }

        if (maximumResponseBytes is < 64 * 1024 or > 8 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes));
        }

        if (maximumRobotsBytes is < 500 * 1024 or > 2 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRobotsBytes));
        }

        if (minimumExtractedCharacters is < 100 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumExtractedCharacters));
        }

        if (maximumExtractedCharacters is < 1_000 or > 100_000
            || maximumExtractedCharacters <= minimumExtractedCharacters)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumExtractedCharacters));
        }

        if (maximumRedirects is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRedirects));
        }

        ValidateCacheDuration(
            successCacheDuration,
            TimeSpan.FromHours(1),
            TimeSpan.FromDays(30),
            nameof(successCacheDuration));
        ValidateCacheDuration(
            unavailableCacheDuration,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromDays(1),
            nameof(unavailableCacheDuration));
        ValidateCacheDuration(
            robotsCacheDuration,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromDays(1),
            nameof(robotsCacheDuration));

        RequestTimeout = requestTimeout;
        MaximumResponseBytes = maximumResponseBytes;
        MaximumRobotsBytes = maximumRobotsBytes;
        MinimumExtractedCharacters = minimumExtractedCharacters;
        MaximumExtractedCharacters = maximumExtractedCharacters;
        MaximumRedirects = maximumRedirects;
        AllowPrivateNetworkTargets = allowPrivateNetworkTargets;
        SuccessCacheDuration = successCacheDuration;
        UnavailableCacheDuration = unavailableCacheDuration;
        RobotsCacheDuration = robotsCacheDuration;
    }

    public TimeSpan RequestTimeout { get; }

    public int MaximumResponseBytes { get; }

    public int MaximumRobotsBytes { get; }

    public int MinimumExtractedCharacters { get; }

    public int MaximumExtractedCharacters { get; }

    public int MaximumRedirects { get; }

    public bool AllowPrivateNetworkTargets { get; }

    public TimeSpan SuccessCacheDuration { get; }

    public TimeSpan UnavailableCacheDuration { get; }

    public TimeSpan RobotsCacheDuration { get; }

    private static void ValidateCacheDuration(
        TimeSpan value,
        TimeSpan minimum,
        TimeSpan maximum,
        string parameterName)
    {
        if (value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
