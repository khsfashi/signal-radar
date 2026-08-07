using SignalRadar.Application.Feeds;
using Xunit;

namespace SignalRadar.Application.Tests.Feeds;

public sealed class FeedCollectionTests
{
    [Fact]
    public void FailureSchedule_QuarantinesAfterFifthConsecutiveFailure()
    {
        DateTimeOffset failedAt = new(2026, 8, 6, 0, 0, 0, TimeSpan.Zero);

        FeedFailureSchedule fourth = FeedFailureSchedule.Calculate(4, failedAt);
        FeedFailureSchedule fifth = FeedFailureSchedule.Calculate(5, failedAt);

        Assert.Null(fourth.QuarantineUntil);
        Assert.Equal(failedAt.AddHours(1), fourth.NextAttemptAt);
        Assert.Equal(failedAt.AddHours(6), fifth.NextAttemptAt);
        Assert.Equal(fifth.NextAttemptAt, fifth.QuarantineUntil);
    }
}
