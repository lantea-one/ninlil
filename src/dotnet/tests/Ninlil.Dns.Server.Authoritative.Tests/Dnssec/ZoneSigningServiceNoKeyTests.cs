using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Tests.Resolution;
using Ninlil.Dns.Server.Dnssec;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Dnssec;

/// <summary>
///     This class checks what happens to a zone that is marked signed but has no key left that is
///     not Removed (every key retired, or none ever created): the signing service returns
///     <see langword="null" />, and <see cref="SignedZoneCache" /> drops the zone's old signed
///     data, keeps the zone listed for re-signing and serves it unsigned. Each test builds a
///     SQLite <see cref="AuthoritativeTestZoneHost" /> and reloads the cache by hand, as the
///     zone-changed event does after a key change.
/// </summary>
public sealed class ZoneSigningServiceNoKeyTests
{
    /// <summary>
    ///     The name of the zone the tests sign.
    /// </summary>
    private const string ZoneName = "nokeys.test";

    /// <summary>
    ///     This method retires every key of a zone through the key management service, the way
    ///     <c>DELETE</c> and <c>IsRetired</c> on the keys API do.
    /// </summary>
    private static async Task RetireAllKeysAsync(IServiceProvider services, Guid zoneId)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        IDnssecKeyRepository keyRepository =
            scope.ServiceProvider.GetRequiredService<IDnssecKeyRepository>();
        IDnssecKeyManagementService keyManagement =
            scope.ServiceProvider.GetRequiredService<IDnssecKeyManagementService>();

        foreach (DnssecKeyEntity key in await keyRepository.ListByZoneIdAsync(zoneId))
            await keyManagement.RetireKeyAsync(key.Id);
    }

    [Fact]
    public async Task Reload_AfterEveryKeyIsRemoved_ServesTheZoneUnsignedAndKeepsItListed()
    {
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        Guid zoneId = await host.AddZoneAsync(
            ZoneName,
            [TestRecord.A($"www.{ZoneName}", "192.0.2.1")],
            TestZoneSigning.Nsec
        );
        await host.StartAsync();
        SignedZoneCache cache = host.Services.GetRequiredService<SignedZoneCache>();
        Assert.NotNull(cache.GetSignedData(zoneId));

        await RetireAllKeysAsync(host.Services, zoneId);
        await cache.ReloadZoneAsync(zoneId);

        Assert.Null(cache.GetSignedData(zoneId));
        Assert.Contains(zoneId, cache.CachedZoneIds);
    }

    [Fact]
    public async Task SignZoneAsync_WithNoKeyAtAll_ReturnsNull()
    {
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        Guid zoneId = await host.AddZoneAsync(
            ZoneName,
            [],
            TestZoneSigning.Nsec,
            generateKeys: false
        );

        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        IZoneSigningService signingService =
            scope.ServiceProvider.GetRequiredService<IZoneSigningService>();

        Assert.Null(await signingService.SignZoneAsync(zoneId));
    }

    [Fact]
    public async Task Reload_AfterAKeyIsAddedToAZoneWithNoKey_ServesTheZoneSigned()
    {
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        Guid zoneId = await host.AddZoneAsync(
            ZoneName,
            [],
            TestZoneSigning.Nsec,
            generateKeys: false
        );
        await host.StartAsync();
        SignedZoneCache cache = host.Services.GetRequiredService<SignedZoneCache>();
        Assert.Null(cache.GetSignedData(zoneId));
        Assert.Contains(zoneId, cache.CachedZoneIds);

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            IDnssecKeyManagementService keyManagement =
                scope.ServiceProvider.GetRequiredService<IDnssecKeyManagementService>();
            await keyManagement.GenerateKeyAsync(
                zoneId,
                DnssecKeyRoleEnum.Ksk,
                DnssecAlgorithmEnum.Ed25519
            );
        }

        await cache.ReloadZoneAsync(zoneId);

        Assert.NotNull(cache.GetSignedData(zoneId));
    }
}
