using Ninlil.Web.Services;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="TwoFactorCodeAttemptLimiter" /> on its own, with a manual
///     clock and no host: the sixth attempt after five wrong codes is blocked with a positive
///     wait, the block ends when the window rolls over, a correct code clears the count, and
///     users do not share a budget.
/// </summary>
public class TwoFactorCodeAttemptLimiterTests
{
    /// <summary>
    ///     This class is a clock that only moves when a test advances it.
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static void Fail(TwoFactorCodeAttemptLimiter limiter, Guid userId, int times)
    {
        for (int i = 0; i < times; i++)
            limiter.RecordFailure(userId);
    }

    [Fact]
    public void TheSixthAttemptAfterFiveFailuresIsBlockedWithAPositiveWait()
    {
        TwoFactorCodeAttemptLimiter limiter = new(new ManualTimeProvider());
        Guid userId = Guid.NewGuid();

        Fail(limiter, userId, 4);
        Assert.False(limiter.IsBlocked(userId, out _));

        limiter.RecordFailure(userId);
        Assert.True(limiter.IsBlocked(userId, out TimeSpan retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
        Assert.True(retryAfter <= TwoFactorCodeAttemptLimiter.Window);
    }

    [Fact]
    public void TheUserIsUnblockedOnceTheWindowRollsOver()
    {
        ManualTimeProvider clock = new();
        TwoFactorCodeAttemptLimiter limiter = new(clock);
        Guid userId = Guid.NewGuid();
        Fail(limiter, userId, 5);

        clock.Advance(TwoFactorCodeAttemptLimiter.Window - TimeSpan.FromSeconds(1));
        Assert.True(limiter.IsBlocked(userId, out TimeSpan retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(1), retryAfter);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.False(limiter.IsBlocked(userId, out _));

        // A failure after the rollover opens a fresh window instead of adding to the old one.
        Fail(limiter, userId, 4);
        Assert.False(limiter.IsBlocked(userId, out _));
    }

    [Fact]
    public void ASuccessClearsTheFailureCount()
    {
        TwoFactorCodeAttemptLimiter limiter = new(new ManualTimeProvider());
        Guid userId = Guid.NewGuid();
        Fail(limiter, userId, 4);

        limiter.RecordSuccess(userId);
        Fail(limiter, userId, 4);

        Assert.False(limiter.IsBlocked(userId, out _));
    }

    [Fact]
    public void UsersHaveIndependentBudgets()
    {
        TwoFactorCodeAttemptLimiter limiter = new(new ManualTimeProvider());
        Guid blocked = Guid.NewGuid();
        Guid other = Guid.NewGuid();

        Fail(limiter, blocked, 5);

        Assert.True(limiter.IsBlocked(blocked, out _));
        Assert.False(limiter.IsBlocked(other, out _));
    }
}
