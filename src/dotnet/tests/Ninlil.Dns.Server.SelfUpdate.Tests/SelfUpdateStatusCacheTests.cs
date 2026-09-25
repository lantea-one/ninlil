using Ninlil.Dns.Server.SelfUpdate.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate.Tests;

/// <summary>
///     This class tests <see cref="SelfUpdateStatusCache" />: the first read returns an empty
///     status built from the current version, and <see cref="ISelfUpdateStatusCache.Update" />
///     replaces what later reads return.
/// </summary>
public class SelfUpdateStatusCacheTests
{
    [Fact]
    public void Current_BeforeAnyUpdate_ReturnsAnEmptySnapshotSeededFromTheCurrentVersion()
    {
        Version currentVersion = new(1, 2, 3, 4);
        SelfUpdateStatusCache cache = new(new FakeCurrentAssemblyVersionProvider(currentVersion));

        SelfUpdateStatus status = cache.Current;

        Assert.False(status.Configured);
        Assert.False(status.UpdateAvailable);
        Assert.Equal(currentVersion, status.CurrentVersion);
        Assert.Null(status.LatestKnownVersion);
        Assert.Null(status.LastCheckedAtUtc);
    }

    [Fact]
    public void Update_ReplacesWhatALaterReadReturns()
    {
        SelfUpdateStatusCache cache = new(
            new FakeCurrentAssemblyVersionProvider(new Version(1, 0, 0))
        );

        SelfUpdateStatus fresh = new(
            true,
            new Version(1, 0, 0),
            new Version(2, 0, 0),
            true,
            "https://example.com/2.0.0",
            "Notes.",
            DateTimeOffset.UtcNow,
            null
        );
        cache.Update(fresh);

        Assert.Same(fresh, cache.Current);
    }
}
