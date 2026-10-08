using Microsoft.Extensions.Caching.Memory;
using Ninlil.Web.Services;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="PendingTwoFactorSignInCache" /> on a real in-memory cache
///     with no host: a token redeems once, an unknown or blank token never redeems, and many
///     threads racing to redeem one token produce exactly one winner.
/// </summary>
public class PendingTwoFactorSignInCacheTests
{
    private static PendingTwoFactorSignInCache CreateCache() =>
        new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public void ATokenRedeemsOnce()
    {
        PendingTwoFactorSignInCache cache = CreateCache();
        Guid userId = Guid.NewGuid();
        string token = cache.Create(userId);

        Assert.Equal(userId, cache.Consume(token));
        Assert.Null(cache.Consume(token));
    }

    [Fact]
    public void PeekDoesNotRedeemTheToken()
    {
        PendingTwoFactorSignInCache cache = CreateCache();
        Guid userId = Guid.NewGuid();
        string token = cache.Create(userId);

        Assert.Equal(userId, cache.Peek(token));
        Assert.Equal(userId, cache.Peek(token));
        Assert.Equal(userId, cache.Consume(token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    public void ABlankOrUnknownTokenNeverRedeems(string token)
    {
        PendingTwoFactorSignInCache cache = CreateCache();

        Assert.Null(cache.Consume(token));
        Assert.Null(cache.Peek(token));
    }

    [Fact]
    public async Task ConcurrentConsumesOfOneTokenYieldExactlyOneSuccess()
    {
        PendingTwoFactorSignInCache cache = CreateCache();

        // Repeat the race, since a single round can pass by luck.
        for (int round = 0; round < 200; round++)
        {
            string token = cache.Create(Guid.NewGuid());
            using ManualResetEventSlim start = new(false);

            Task<Guid?>[] attempts = Enumerable
                .Range(0, 16)
                .Select(_ =>
                    Task.Run(() =>
                    {
                        start.Wait();
                        return cache.Consume(token);
                    })
                )
                .ToArray();
            start.Set();
            Guid?[] results = await Task.WhenAll(attempts);

            Assert.Equal(1, results.Count(result => result is not null));
        }
    }
}
