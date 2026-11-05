using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Tests.Resolution;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Recursive.Validation;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Dnssec;

/// <summary>
///     This class tests that a key change in a child zone updates the DS RRset of a signed parent
///     hosted on the same server without any other change. The tests make the change the way an
///     operator does: they create or retire a key through <see cref="IDnssecKeyManagementService" />
///     and publish <see cref="DnsZoneChangedEvent" />, as the key REST endpoints do, then wait for
///     the event bus to deliver it. Each test runs a SQLite <see cref="AuthoritativeTestZoneHost" />
///     with the parent <c>example.test</c> (which delegates <c>sub.example.test</c>) and the signed
///     child <c>sub.example.test</c>, and checks the parent's signed data in
///     <see cref="SignedZoneCache" /> (DS records and the RRSIG over them).
/// </summary>
public sealed class ParentDsFollowsChildKeyChangeTests
{
    private const string ParentName = "example.test";
    private const string ChildName = "sub.example.test";

    /// <summary>
    ///     This method builds a started host with a signed parent and a signed child, each with
    ///     one KSK and one ZSK.
    /// </summary>
    private static async Task<(
        AuthoritativeTestZoneHost Host,
        Guid ParentId,
        Guid ChildId
    )> StartHostAsync()
    {
        AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        Guid parentId = await host.AddZoneAsync(
            ParentName,
            [TestRecord.Ns(ChildName, $"ns.{ChildName}.")],
            TestZoneSigning.Nsec
        );
        Guid childId = await host.AddZoneAsync(ChildName, [], TestZoneSigning.Nsec);
        await host.StartAsync();

        return (host, parentId, childId);
    }

    /// <summary>
    ///     This method creates a key in the child and announces the change, like the key create
    ///     endpoint does.
    /// </summary>
    private static async Task<DnssecKeyEntity> CreateKeyAsync(
        AuthoritativeTestZoneHost host,
        Guid childId,
        DnssecKeyRoleEnum role
    )
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        DnssecKeyEntity key = await scope
            .ServiceProvider.GetRequiredService<IDnssecKeyManagementService>()
            .GenerateKeyAsync(childId, role, DnssecAlgorithmEnum.Ed25519);
        host.Services.GetRequiredService<IDnsServerEventPublisher>()
            .Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, childId, ChildName));

        return key;
    }

    /// <summary>
    ///     This method waits until the parent's served DS key tags for the child satisfy a
    ///     condition, and returns them. It fails the test after ten seconds.
    /// </summary>
    private static async Task<List<ushort>> WaitForDsKeyTagsAsync(
        AuthoritativeTestZoneHost host,
        Guid parentId,
        Func<List<ushort>, bool> condition,
        string childName = ChildName
    )
    {
        SignedZoneCache cache = host.Services.GetRequiredService<SignedZoneCache>();
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        List<ushort> keyTags = [];

        while (DateTimeOffset.UtcNow < deadline)
        {
            keyTags = DsKeyTags(cache.GetSignedData(parentId), childName);

            if (condition(keyTags))
                return keyTags;

            await Task.Delay(50);
        }

        Assert.Fail(
            $"The parent's DS key tags never matched; last seen: {string.Join(',', keyTags)}"
        );
        return keyTags;
    }

    /// <summary>
    ///     This method returns the key tags of the DS RRset the parent serves for the child.
    /// </summary>
    private static List<ushort> DsKeyTags(SignedZoneData signed, string childName = ChildName) =>
        signed.DsRecordsByDelegationName.TryGetValue(
            DnsName.Parse(childName),
            out IReadOnlyList<DnsResourceRecord> ds
        )
            ?
            [
                .. ds.Select(record =>
                    (
                        (DsRecordData)
                            DnsRecordDataCodecRegistry.Decode(
                                DnsResourceRecordTypeEnum.DS,
                                record.Data
                            )
                    ).KeyTag
                ),
            ]
            : [];

    /// <summary>
    ///     This method asserts that the parent's DS RRset for the child carries a valid RRSIG
    ///     made with the parent's own keys (RFC 4035 §2.4: the parent signs the DS RRset).
    /// </summary>
    private static void AssertDsRrsetIsSigned(SignedZoneData signed)
    {
        DnsName child = DnsName.Parse(ChildName);
        IReadOnlyList<DnsResourceRecord> ds = signed.DsRecordsByDelegationName[child];

        Assert.True(
            RrsetSignatureVerifier.TryVerify(
                ds,
                signed.GetSignatures(child, DnsResourceRecordTypeEnum.DS),
                signed.DnsKeyRecords,
                DateTimeOffset.UtcNow
            )
        );
    }

    /// <summary>
    ///     This method returns the child's Active KSK that exists before a test changes anything.
    /// </summary>
    private static async Task<DnssecKeyEntity> FindFirstKskAsync(
        AuthoritativeTestZoneHost host,
        Guid childId
    )
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        IReadOnlyList<DnssecKeyEntity> keys = await scope
            .ServiceProvider.GetRequiredService<IDnssecKeyRepository>()
            .ListByZoneIdAsync(childId);

        return keys.Single(key => key.Role == DnssecKeyRoleEnum.Ksk);
    }

    [Fact]
    public async Task CreatingAKskInTheChild_AddsItsDsToTheSignedParent()
    {
        (AuthoritativeTestZoneHost host, Guid parentId, Guid childId) = await StartHostAsync();
        await using AuthoritativeTestZoneHost _ = host;
        SignedZoneCache cache = host.Services.GetRequiredService<SignedZoneCache>();
        DnssecKeyEntity first = await FindFirstKskAsync(host, childId);
        Assert.Equal([first.KeyTag], DsKeyTags(cache.GetSignedData(parentId)));

        DnssecKeyEntity added = await CreateKeyAsync(host, childId, DnssecKeyRoleEnum.Ksk);

        List<ushort> tags = await WaitForDsKeyTagsAsync(host, parentId, list => list.Count == 2);
        Assert.Contains(first.KeyTag, tags);
        Assert.Contains(added.KeyTag, tags);
        AssertDsRrsetIsSigned(cache.GetSignedData(parentId));
    }

    [Fact]
    public async Task RemovingAKskFromTheChild_RemovesItsDsFromTheSignedParent()
    {
        (AuthoritativeTestZoneHost host, Guid parentId, Guid childId) = await StartHostAsync();
        await using AuthoritativeTestZoneHost _ = host;
        SignedZoneCache cache = host.Services.GetRequiredService<SignedZoneCache>();
        DnssecKeyEntity first = await FindFirstKskAsync(host, childId);
        DnssecKeyEntity added = await CreateKeyAsync(host, childId, DnssecKeyRoleEnum.Ksk);
        await WaitForDsKeyTagsAsync(host, parentId, list => list.Count == 2);

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IDnssecKeyManagementService>()
                .RetireKeyAsync(added.Id);
        }
        host.Services.GetRequiredService<IDnsServerEventPublisher>()
            .Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, childId, ChildName));

        List<ushort> tags = await WaitForDsKeyTagsAsync(host, parentId, list => list.Count == 1);
        Assert.Equal([first.KeyTag], tags);
        AssertDsRrsetIsSigned(cache.GetSignedData(parentId));
    }

    [Fact]
    public async Task RemovingTheLastKskFromTheChild_LeavesTheSignedParentWithoutADs()
    {
        (AuthoritativeTestZoneHost host, Guid parentId, Guid childId) = await StartHostAsync();
        await using AuthoritativeTestZoneHost _ = host;
        DnssecKeyEntity first = await FindFirstKskAsync(host, childId);

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IDnssecKeyManagementService>()
                .RetireKeyAsync(first.Id);
        }
        host.Services.GetRequiredService<IDnsServerEventPublisher>()
            .Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, childId, ChildName));

        await WaitForDsKeyTagsAsync(host, parentId, list => list.Count == 0);
    }

    [Fact]
    public async Task UnsigningTheChild_RemovesItsDsFromTheSignedParent()
    {
        (AuthoritativeTestZoneHost host, Guid parentId, Guid childId) = await StartHostAsync();
        await using AuthoritativeTestZoneHost _ = host;

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            IZoneRepository zoneRepository =
                scope.ServiceProvider.GetRequiredService<IZoneRepository>();
            ZoneEntity child = await zoneRepository.GetByIdAsync(childId);
            child.IsDnssecSigned = false;
            zoneRepository.Update(child);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }
        host.Services.GetRequiredService<IDnsServerEventPublisher>()
            .Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, childId, ChildName));

        await WaitForDsKeyTagsAsync(host, parentId, list => list.Count == 0);
    }

    /// <summary>
    ///     A ZSK is never in the parent's DS set (RFC 4034 §5 hashes the key a DS names; the
    ///     parent vouches for the KSK), so a ZSK change must not re-sign the parent. The check
    ///     waits for the child to be signed again, which shows the event was handled, then
    ///     compares the parent's signed data by reference.
    /// </summary>
    [Fact]
    public async Task CreatingAZskInTheChild_DoesNotResignTheParent()
    {
        (AuthoritativeTestZoneHost host, Guid parentId, Guid childId) = await StartHostAsync();
        await using AuthoritativeTestZoneHost _ = host;
        SignedZoneCache cache = host.Services.GetRequiredService<SignedZoneCache>();
        SignedZoneData parentBefore = cache.GetSignedData(parentId);
        SignedZoneData childBefore = cache.GetSignedData(childId);

        await CreateKeyAsync(host, childId, DnssecKeyRoleEnum.Zsk);

        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (
            ReferenceEquals(cache.GetSignedData(childId), childBefore)
            && DateTimeOffset.UtcNow < deadline
        )
            await Task.Delay(50);

        Assert.NotSame(childBefore, cache.GetSignedData(childId));
        Assert.Same(parentBefore, cache.GetSignedData(parentId));
    }

    /// <summary>
    ///     This method reads the SOA serial stored for a zone.
    /// </summary>
    private static async Task<uint> ReadSerialAsync(AuthoritativeTestZoneHost host, Guid zoneId)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        IReadOnlyList<RecordEntity> records = await scope
            .ServiceProvider.GetRequiredService<IRecordRepository>()
            .ListByZoneIdAsync(zoneId);

        return (
            (SoaRecordData)
                RecordEntityCodec.DecodeTypedData(
                    records.Single(record => record.Type == DnsResourceRecordTypeEnum.Soa)
                )
        ).Serial;
    }

    /// <summary>
    ///     A secondary follows the parent by SOA serial (RFC 1982 serial arithmetic, RFC 1996
    ///     NOTIFY), so a new DS RRset needs a new serial.
    /// </summary>
    [Fact]
    public async Task CreatingAKskInTheChild_AdvancesTheParentsSoaSerial()
    {
        (AuthoritativeTestZoneHost host, Guid parentId, Guid childId) = await StartHostAsync();
        await using AuthoritativeTestZoneHost _ = host;
        uint before = await ReadSerialAsync(host, parentId);

        await CreateKeyAsync(host, childId, DnssecKeyRoleEnum.Ksk);
        await WaitForDsKeyTagsAsync(host, parentId, list => list.Count == 2);

        Assert.True(await ReadSerialAsync(host, parentId) > before);
    }

    [Fact]
    public async Task DeletingTheSignedChild_RemovesItsDsFromTheSignedParent()
    {
        (AuthoritativeTestZoneHost host, Guid parentId, Guid childId) = await StartHostAsync();
        await using AuthoritativeTestZoneHost _ = host;

        // The order the zone delete endpoint uses: keys, then records and zone.
        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            IDnssecKeyRepository keyRepository =
                scope.ServiceProvider.GetRequiredService<IDnssecKeyRepository>();
            foreach (DnssecKeyEntity key in await keyRepository.ListByZoneIdAsync(childId))
                keyRepository.Remove(key);

            IZoneRepository zoneRepository =
                scope.ServiceProvider.GetRequiredService<IZoneRepository>();
            zoneRepository.Remove(await zoneRepository.GetByIdAsync(childId));
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }
        host.Services.GetRequiredService<IDnsServerEventPublisher>()
            .Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, childId, ChildName));

        await WaitForDsKeyTagsAsync(host, parentId, list => list.Count == 0);
    }

    /// <summary>
    ///     Renaming a signed child moves its DS: the parent of the new name gains one and the
    ///     parent of the old name drops it. The parent holds delegations for both names.
    /// </summary>
    [Fact]
    public async Task RenamingTheSignedChild_MovesItsDsInTheParent()
    {
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        Guid parentId = await host.AddZoneAsync(
            ParentName,
            [
                TestRecord.Ns(ChildName, $"ns.{ChildName}."),
                TestRecord.Ns("other.example.test", "ns.other.example.test."),
            ],
            TestZoneSigning.Nsec
        );
        Guid childId = await host.AddZoneAsync(ChildName, [], TestZoneSigning.Nsec);
        await host.StartAsync();

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            IZoneRepository zoneRepository =
                scope.ServiceProvider.GetRequiredService<IZoneRepository>();
            ZoneEntity child = await zoneRepository.GetByIdAsync(childId);
            child.Name = "other.example.test";
            zoneRepository.Update(child);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }
        host.Services.GetRequiredService<IDnsServerEventPublisher>()
            .Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, childId, "other.example.test"));

        await WaitForDsKeyTagsAsync(host, parentId, list => list.Count == 1, "other.example.test");
        await WaitForDsKeyTagsAsync(host, parentId, list => list.Count == 0);
    }

    /// <summary>
    ///     With example.test, a.example.test and b.a.example.test all hosted, a KSK change in
    ///     b.a.example.test re-signs its closest parent a.example.test, not example.test.
    /// </summary>
    [Fact]
    public async Task CreatingAKskInAGrandchild_ResignsOnlyTheClosestParent()
    {
        await using AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
        Guid grandparentId = await host.AddZoneAsync(
            ParentName,
            [TestRecord.Ns("a.example.test", "ns.a.example.test.")],
            TestZoneSigning.Nsec
        );
        Guid middleId = await host.AddZoneAsync(
            "a.example.test",
            [TestRecord.Ns("b.a.example.test", "ns.b.a.example.test.")],
            TestZoneSigning.Nsec
        );
        Guid leafId = await host.AddZoneAsync("b.a.example.test", [], TestZoneSigning.Nsec);
        await host.StartAsync();
        SignedZoneCache cache = host.Services.GetRequiredService<SignedZoneCache>();
        SignedZoneData grandparentBefore = cache.GetSignedData(grandparentId);

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IDnssecKeyManagementService>()
                .GenerateKeyAsync(leafId, DnssecKeyRoleEnum.Ksk, DnssecAlgorithmEnum.Ed25519);
        }
        host.Services.GetRequiredService<IDnsServerEventPublisher>()
            .Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, leafId, "b.a.example.test"));

        await WaitForDsKeyTagsAsync(host, middleId, list => list.Count == 2, "b.a.example.test");

        // Give a wrongly triggered grandparent re-sign time to happen.
        await Task.Delay(500);
        Assert.Same(grandparentBefore, cache.GetSignedData(grandparentId));
    }
}
