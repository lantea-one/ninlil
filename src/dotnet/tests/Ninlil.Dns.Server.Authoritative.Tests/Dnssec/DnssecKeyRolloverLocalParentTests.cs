using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Tests.Resolution;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Dnssec;

/// <summary>
///     This class checks <see cref="DnssecKeyRolloverBackgroundService.FindLocalParent" />, the
///     lookup that decides whether a KSK rollover may promote the incoming key at once (the
///     parent is hosted and signed here and regenerates its DS) or must wait for an operator to
///     confirm the parent's DS, and how long the outgoing KSK is kept (the parent's DS TTL). Each
///     test builds a SQLite <see cref="AuthoritativeTestZoneHost" /> with both zone caches
///     started and calls the method on the hosted service resolved from the container. No timers
///     run: the method reads the in-memory zone cache only.
/// </summary>
public sealed class DnssecKeyRolloverLocalParentTests
{
    /// <summary>
    ///     This method builds the DS record data a test stores at a delegation. The values are
    ///     arbitrary: the lookup reads only the record's owner, type and TTL.
    /// </summary>
    private static TestRecord Ds(string owner, uint ttl) =>
        new(
            owner,
            DnsResourceRecordTypeEnum.DS,
            new DsRecordData(12345, DnssecAlgorithmEnum.Ed25519, 2, new byte[32]),
            ttl
        );

    /// <summary>
    ///     This method changes a zone already added to the host to another type, before the
    ///     host starts.
    /// </summary>
    private static async Task SetZoneTypeAsync(
        AuthoritativeTestZoneHost host,
        Guid zoneId,
        ZoneTypeEnum type
    )
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        ZoneEntity zone = await zoneRepository.GetByIdAsync(zoneId);
        zone.Type = type;
        zoneRepository.Update(zone);
        await unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    ///     This method runs <see cref="DnssecKeyRolloverBackgroundService.FindLocalParent" /> for
    ///     the zone with the given identifier.
    /// </summary>
    private static async Task<(
        bool IsLocalParent,
        Guid? ParentZoneId,
        TimeSpan DsTtl
    )> FindLocalParentAsync(AuthoritativeTestZoneHost host, Guid childZoneId)
    {
        DnssecKeyRolloverBackgroundService service = host
            .Services.GetServices<IHostedService>()
            .OfType<DnssecKeyRolloverBackgroundService>()
            .Single();

        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        ZoneEntity child = await scope
            .ServiceProvider.GetRequiredService<IZoneRepository>()
            .GetByIdAsync(childZoneId);

        return service.FindLocalParent(child);
    }

    [Fact]
    public async Task FindLocalParent_ForAParentTwoLabelsUp_FindsIt()
    {
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        Guid parentId = await host.AddZoneAsync(
            "example.test",
            [TestRecord.Ns("a.b.example.test", "ns.a.b.example.test.")],
            TestZoneSigning.Nsec
        );
        Guid childId = await host.AddZoneAsync("a.b.example.test", [], TestZoneSigning.Nsec);
        await host.StartAsync();

        (bool isLocalParent, Guid? parentZoneId, TimeSpan dsTtl) = await FindLocalParentAsync(
            host,
            childId
        );

        Assert.True(isLocalParent);
        Assert.Equal(parentId, parentZoneId);
        Assert.Equal(TimeSpan.FromSeconds(3600), dsTtl);
    }

    [Fact]
    public async Task FindLocalParent_WithAnIntermediateHostedZone_FindsTheNearestOne()
    {
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        await host.AddZoneAsync(
            "example.test",
            [TestRecord.Ns("b.example.test", "ns.b.example.test.")],
            TestZoneSigning.Nsec
        );
        Guid nearestId = await host.AddZoneAsync(
            "b.example.test",
            [TestRecord.Ns("a.b.example.test", "ns.a.b.example.test.")],
            TestZoneSigning.Nsec
        );
        Guid childId = await host.AddZoneAsync("a.b.example.test", [], TestZoneSigning.Nsec);
        await host.StartAsync();

        (bool isLocalParent, Guid? parentZoneId, _) = await FindLocalParentAsync(host, childId);

        Assert.True(isLocalParent);
        Assert.Equal(nearestId, parentZoneId);
    }

    [Fact]
    public async Task FindLocalParent_WhenTheNearestZoneIsAnUnsignedPrimary_IsExternal()
    {
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        await host.AddZoneAsync(
            "example.test",
            [TestRecord.Ns("a.b.example.test", "ns.a.b.example.test.")],
            TestZoneSigning.None
        );
        Guid childId = await host.AddZoneAsync("a.b.example.test", [], TestZoneSigning.Nsec);
        await host.StartAsync();

        (bool isLocalParent, Guid? parentZoneId, _) = await FindLocalParentAsync(host, childId);

        Assert.False(isLocalParent);
        Assert.Null(parentZoneId);
    }

    [Fact]
    public async Task FindLocalParent_WhenTheNearestZoneIsASecondary_IsExternal()
    {
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        Guid parentId = await host.AddZoneAsync(
            "example.test",
            [TestRecord.Ns("a.b.example.test", "ns.a.b.example.test.")],
            TestZoneSigning.Nsec
        );
        await SetZoneTypeAsync(host, parentId, ZoneTypeEnum.Secondary);
        Guid childId = await host.AddZoneAsync("a.b.example.test", [], TestZoneSigning.Nsec);
        await host.StartAsync();

        (bool isLocalParent, Guid? parentZoneId, _) = await FindLocalParentAsync(host, childId);

        Assert.False(isLocalParent);
        Assert.Null(parentZoneId);
    }

    [Fact]
    public async Task FindLocalParent_WhenTheChildIsBelowAHigherCut_IsExternal()
    {
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        await host.AddZoneAsync(
            "example.test",
            [
                TestRecord.Ns("b.example.test", "ns.other.example."),
                TestRecord.Ns("a.b.example.test", "ns.a.b.example.test."),
            ],
            TestZoneSigning.Nsec
        );
        Guid childId = await host.AddZoneAsync("a.b.example.test", [], TestZoneSigning.Nsec);
        await host.StartAsync();

        (bool isLocalParent, Guid? parentZoneId, _) = await FindLocalParentAsync(host, childId);

        Assert.False(isLocalParent);
        Assert.Null(parentZoneId);
    }

    [Fact]
    public async Task FindLocalParent_WhenTheParentHasNoNsForTheChild_IsExternal()
    {
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        await host.AddZoneAsync(
            "example.test",
            [TestRecord.A("www.example.test", "192.0.2.1")],
            TestZoneSigning.Nsec
        );
        Guid childId = await host.AddZoneAsync("a.b.example.test", [], TestZoneSigning.Nsec);
        await host.StartAsync();

        (bool isLocalParent, _, _) = await FindLocalParentAsync(host, childId);

        Assert.False(isLocalParent);
    }

    [Fact]
    public async Task FindLocalParent_ForATopLevelChild_IsExternal()
    {
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        Guid childId = await host.AddZoneAsync("test", [], TestZoneSigning.Nsec);
        await host.StartAsync();

        (bool isLocalParent, _, _) = await FindLocalParentAsync(host, childId);

        Assert.False(isLocalParent);
    }

    [Fact]
    public async Task FindLocalParent_WithAStoredDsOfALongerTtl_UsesThatTtl()
    {
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        await host.AddZoneAsync(
            "example.test",
            [
                TestRecord.Ns("a.b.example.test", "ns.a.b.example.test."),
                Ds("a.b.example.test", 86400),
            ],
            TestZoneSigning.Nsec
        );
        Guid childId = await host.AddZoneAsync("a.b.example.test", [], TestZoneSigning.Nsec);
        await host.StartAsync();

        (bool isLocalParent, _, TimeSpan dsTtl) = await FindLocalParentAsync(host, childId);

        Assert.True(isLocalParent);
        Assert.Equal(TimeSpan.FromSeconds(86400), dsTtl);
    }

    [Fact]
    public async Task FindLocalParent_WithAStoredDsOfAShorterTtl_KeepsTheFloor()
    {
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        await host.AddZoneAsync(
            "example.test",
            [TestRecord.Ns("a.b.example.test", "ns.a.b.example.test."), Ds("a.b.example.test", 60)],
            TestZoneSigning.Nsec
        );
        Guid childId = await host.AddZoneAsync("a.b.example.test", [], TestZoneSigning.Nsec);
        await host.StartAsync();

        (_, _, TimeSpan dsTtl) = await FindLocalParentAsync(host, childId);

        Assert.Equal(TimeSpan.FromSeconds(3600), dsTtl);
    }
}
