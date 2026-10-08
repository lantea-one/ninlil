using Ninlil.Web.Services;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="TwoFactorEmailCodeCooldown" /> on its own, with a manual
///     clock and no host: the 60-second cooldown per user, the cap of three sends per pending
///     token, that a signed-in caller has no per-token cap, that users are independent, and that
///     of parallel requests exactly one wins.
/// </summary>
public class TwoFactorEmailCodeCooldownTests
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

    [Fact]
    public void ASecondSendWithinTheCooldownIsRefusedWithTheRemainingTime()
    {
        ManualTimeProvider clock = new();
        TwoFactorEmailCodeCooldown cooldown = new(clock);
        Guid user = Guid.NewGuid();

        Assert.True(cooldown.TryAcquire(user, "t", out _));
        clock.Advance(TimeSpan.FromSeconds(20));

        Assert.False(cooldown.TryAcquire(user, "t", out TimeSpan retryAfter));
        Assert.Equal(TimeSpan.FromSeconds(40), retryAfter);
    }

    [Fact]
    public void ASendAfterTheCooldownIsAllowed()
    {
        ManualTimeProvider clock = new();
        TwoFactorEmailCodeCooldown cooldown = new(clock);
        Guid user = Guid.NewGuid();

        Assert.True(cooldown.TryAcquire(user, "t", out _));
        clock.Advance(TwoFactorEmailCodeCooldown.Cooldown);

        Assert.True(cooldown.TryAcquire(user, "t", out _));
    }

    [Fact]
    public void TheFourthSendOnOneTokenIsRefusedEvenAfterTheCooldown()
    {
        ManualTimeProvider clock = new();
        TwoFactorEmailCodeCooldown cooldown = new(clock);
        Guid user = Guid.NewGuid();

        for (int i = 0; i < TwoFactorEmailCodeCooldown.MaxSendsPerPendingToken; i++)
        {
            Assert.True(cooldown.TryAcquire(user, "t", out _));
            clock.Advance(TwoFactorEmailCodeCooldown.Cooldown);
        }

        Assert.False(cooldown.TryAcquire(user, "t", out TimeSpan retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
        // A different pending token for the same user starts a fresh count.
        Assert.True(cooldown.TryAcquire(user, "other", out _));
    }

    [Fact]
    public void ANullTokenHasNoPerTokenCap()
    {
        ManualTimeProvider clock = new();
        TwoFactorEmailCodeCooldown cooldown = new(clock);
        Guid user = Guid.NewGuid();

        for (int i = 0; i < 10; i++)
        {
            Assert.True(cooldown.TryAcquire(user, null, out _));
            clock.Advance(TwoFactorEmailCodeCooldown.Cooldown);
        }
    }

    [Fact]
    public void ASignedInSendStartsTheCooldownForTheSignInStep()
    {
        TwoFactorEmailCodeCooldown cooldown = new(new ManualTimeProvider());
        Guid user = Guid.NewGuid();

        Assert.True(cooldown.TryAcquire(user, null, out _));
        Assert.False(cooldown.TryAcquire(user, "t", out _));
    }

    [Fact]
    public void ARefusedRequestIsNotCounted()
    {
        ManualTimeProvider clock = new();
        TwoFactorEmailCodeCooldown cooldown = new(clock);
        Guid user = Guid.NewGuid();

        Assert.True(cooldown.TryAcquire(user, "t", out _));
        for (int i = 0; i < 5; i++)
            Assert.False(cooldown.TryAcquire(user, "t", out _));
        clock.Advance(TwoFactorEmailCodeCooldown.Cooldown);

        Assert.True(cooldown.TryAcquire(user, "t", out _));
    }

    [Fact]
    public void UsersAreIndependent()
    {
        TwoFactorEmailCodeCooldown cooldown = new(new ManualTimeProvider());

        Assert.True(cooldown.TryAcquire(Guid.NewGuid(), "a", out _));
        Assert.True(cooldown.TryAcquire(Guid.NewGuid(), "b", out _));
    }

    [Fact]
    public async Task OfParallelRequestsExactlyOneWins()
    {
        TwoFactorEmailCodeCooldown cooldown = new(new ManualTimeProvider());
        Guid user = Guid.NewGuid();

        bool[] results = await Task.WhenAll(
            Enumerable
                .Range(0, 32)
                .Select(i => Task.Run(() => cooldown.TryAcquire(user, "t", out TimeSpan _)))
        );

        Assert.Single(results, won => won);
    }
}
