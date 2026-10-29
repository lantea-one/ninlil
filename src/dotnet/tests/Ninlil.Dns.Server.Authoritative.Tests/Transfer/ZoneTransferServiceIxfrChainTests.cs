using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Transfer;

/// <summary>
///     This class tests that <see cref="ZoneTransferService" /> answers an IXFR with the SOA each
///     journaled step really had, and with a full transfer when the SOA history of the window is
///     missing or inconsistent. Each test has its own <see cref="TsigTransferHost" /> (SQLite, no
///     sockets; the zone starts at serial 100 with refresh 3600), makes its changes through the real
///     <see cref="IZoneUpdateService" /> or the host's journal helper, reloads the zone cache and
///     reads the yielded messages directly.
/// </summary>
public class ZoneTransferServiceIxfrChainTests
{
    /// <summary>
    ///     Two steps applied the way an incoming IXFR applies them (a replacement SOA each, the
    ///     second changing refresh and minimum) are served with each step's own SOAs, and applying
    ///     the answer to the starting record set gives the current one.
    /// </summary>
    [Fact]
    public async Task CreateIxfrResponseAsync_AfterTwoReplacementSoaSteps_CarriesEachStepsOwnSoas()
    {
        await using TsigTransferHost host = await TsigTransferHost.CreateAsync(100);
        Guid zoneId = await ZoneIdAsync(host);
        RecordEntity host1 = await FindAsync(host, zoneId, $"host1.{TsigTransferHost.ZoneName}");

        await ApplyAsync(
            host,
            zoneId,
            [ARecord(zoneId, 6)],
            [],
            Soa(110, refresh: 3600, minimum: 3600)
        );
        await ApplyAsync(host, zoneId, [ARecord(zoneId, 7)], [host1], Soa(120, 1800, 60));

        List<DnsResourceRecord> records = await RunIxfrAsync(host, 100);

        // Leading SOA, then per step: old SOA, removed, new SOA, added; then the closing SOA.
        Assert.Equal(9, records.Count);
        Assert.Equal(120u, SoaOf(records[0]).Serial);
        Assert.Equal(1800u, SoaOf(records[0]).RefreshSeconds);

        Assert.Equal(100u, SoaOf(records[1]).Serial);
        Assert.Equal(3600u, SoaOf(records[1]).RefreshSeconds);
        Assert.Equal(110u, SoaOf(records[2]).Serial);
        Assert.Equal(3600u, SoaOf(records[2]).RefreshSeconds);
        Assert.Equal(3600u, SoaOf(records[2]).MinimumTtlSeconds);
        Assert.Equal(HostName(6), records[3].Name);

        // Step 2 starts from step 1's SOA, not from the current one.
        Assert.Equal(110u, SoaOf(records[4]).Serial);
        Assert.Equal(3600u, SoaOf(records[4]).RefreshSeconds);
        Assert.Equal(HostName(1), records[5].Name);
        Assert.Equal(120u, SoaOf(records[6]).Serial);
        Assert.Equal(1800u, SoaOf(records[6]).RefreshSeconds);
        Assert.Equal(60u, SoaOf(records[6]).MinimumTtlSeconds);
        Assert.Equal(HostName(7), records[7].Name);
        Assert.Equal(records[0].Data, records[8].Data);

        // The pull side applies the answer to the starting zone and reaches the current one.
        ZoneTransferResponseAssembler assembler = new(100);
        Assert.Null(assembler.AddMessage(records));
        Assert.True(assembler.IsComplete);
        ZoneTransferPullResult result = assembler.BuildResult();
        Assert.True(result.IsIncremental);

        HashSet<DnsName> names = [.. Enumerable.Range(1, 5).Select(index => HostName(index))];

        foreach (ZoneTransferDiffStep step in result.DiffSteps)
        {
            foreach (DnsResourceRecord removed in step.Removed)
                Assert.True(names.Remove(removed.Name));

            foreach (DnsResourceRecord added in step.Added)
                names.Add(added.Name);
        }

        Assert.Equal(
            new[] { 2, 3, 4, 5, 6, 7 }.Select(number => HostName(number).ToString()).Order(),
            names.Select(name => name.ToString()).Order()
        );
    }

    /// <summary>
    ///     An API edit of the SOA is a step with no records, only the two SOAs: the old one with the
    ///     fields before the edit and the new one with the edited fields.
    /// </summary>
    [Fact]
    public async Task CreateIxfrResponseAsync_AfterAnSoaEdit_CarriesTheOldAndTheEditedSoaAsTheOnlyStepContent()
    {
        await using TsigTransferHost host = await TsigTransferHost.CreateAsync(100);
        Guid zoneId = await ZoneIdAsync(host);
        RecordEntity soa = await FindAsync(
            host,
            zoneId,
            TsigTransferHost.ZoneName,
            DnsResourceRecordTypeEnum.Soa
        );

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IZoneUpdateService>()
                .ApplyRecordEditAsync(
                    zoneId,
                    soa.Id,
                    record =>
                        record.DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                            Soa(100, refresh: 7200, minimum: 3600)
                        )
                );
        }

        await WaitForCachedSerialAsync(host, 101);

        List<DnsResourceRecord> records = await RunIxfrAsync(host, 100);

        // Leading SOA, old SOA, new SOA, closing SOA: no records between them.
        Assert.Equal(4, records.Count);
        Assert.All(records, record => Assert.Equal(DnsResourceRecordTypeEnum.Soa, record.Type));
        Assert.Equal(101u, SoaOf(records[0]).Serial);
        Assert.Equal(7200u, SoaOf(records[0]).RefreshSeconds);
        Assert.Equal(100u, SoaOf(records[1]).Serial);
        Assert.Equal(3600u, SoaOf(records[1]).RefreshSeconds);
        Assert.Equal(101u, SoaOf(records[2]).Serial);
        Assert.Equal(7200u, SoaOf(records[2]).RefreshSeconds);
        Assert.Equal(records[0].Data, records[3].Data);
    }

    /// <summary>
    ///     A window with a step that has no SOA history (a row written before the history existed)
    ///     is answered with the whole zone.
    /// </summary>
    [Fact]
    public async Task CreateIxfrResponseAsync_WhenAStepHasNoSoaHistory_AnswersWithAFullTransfer()
    {
        await using TsigTransferHost host = await TsigTransferHost.CreateAsync(100);
        await host.AddJournalEntryAsync(90, 95);
        await host.AddJournalEntryAsync(95, 100, withSoaHistory: false);

        AssertFullAnswer(await RunIxfrAsync(host, 90));
    }

    /// <summary>
    ///     A window whose last recorded SOA differs from the stored SOA (a write that skipped the
    ///     journal's SOA bookkeeping) is answered with the whole zone.
    /// </summary>
    [Fact]
    public async Task CreateIxfrResponseAsync_WhenTheLastRecordedSoaDiffersFromTheStoredOne_AnswersWithAFullTransfer()
    {
        await using TsigTransferHost host = await TsigTransferHost.CreateAsync(100);
        await host.AddJournalEntryAsync(90, 100, soaRefreshSeconds: 1);

        AssertFullAnswer(await RunIxfrAsync(host, 90));
    }

    /// <summary>
    ///     A window whose serials are contiguous but whose second step starts from an SOA other than
    ///     the first step's new SOA is answered with the whole zone.
    /// </summary>
    [Fact]
    public async Task CreateIxfrResponseAsync_WhenTheSoaChainBreaksBetweenTwoSteps_AnswersWithAFullTransfer()
    {
        await using TsigTransferHost host = await TsigTransferHost.CreateAsync(100);
        await host.AddJournalEntryAsync(90, 95, soaRefreshSeconds: 1);
        await host.AddJournalEntryAsync(95, 100);

        AssertFullAnswer(await RunIxfrAsync(host, 90));
    }

    /// <summary>
    ///     A window of consistent steps ending at the stored SOA stays incremental, so the fallback
    ///     tests above fail for the history and not for the helper.
    /// </summary>
    [Fact]
    public async Task CreateIxfrResponseAsync_WhenEveryStepHasConsistentSoaHistory_StaysIncremental()
    {
        await using TsigTransferHost host = await TsigTransferHost.CreateAsync(100);
        await host.AddJournalEntryAsync(90, 95);
        await host.AddJournalEntryAsync(95, 100);

        List<DnsResourceRecord> records = await RunIxfrAsync(host, 90);

        // Leading SOA, two (old, new) pairs, closing SOA.
        Assert.Equal(6, records.Count);
        Assert.All(records, record => Assert.Equal(DnsResourceRecordTypeEnum.Soa, record.Type));
        Assert.Equal(
            [100u, 90u, 95u, 95u, 100u, 100u],
            records.Select(record => SoaOf(record).Serial).ToArray()
        );
    }

    /// <summary>
    ///     This method applies one incoming-IXFR-style step through <see cref="IZoneUpdateService" />
    ///     and waits until the zone cache serves the step's serial.
    /// </summary>
    private static async Task ApplyAsync(
        TsigTransferHost host,
        Guid zoneId,
        List<RecordEntity> add,
        List<RecordEntity> remove,
        SoaRecordData replacement
    )
    {
        DnsResourceRecord replacementWire = new(
            DnsName.Parse(TsigTransferHost.ZoneName),
            DnsResourceRecordTypeEnum.Soa,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.Soa, replacement)
        );

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IZoneUpdateService>()
                .ApplyChangesAsync(
                    zoneId,
                    add,
                    remove,
                    explicitNewSerial: replacement.Serial,
                    replacementSoa: replacementWire
                );
        }

        await WaitForCachedSerialAsync(host, replacement.Serial);
    }

    /// <summary>
    ///     This method waits until the zone cache holds an SOA with <paramref name="serial" />,
    ///     polling for at most 30 seconds. The cache reloads on the zone-changed event that the
    ///     update publishes, on the dispatcher's thread. The test must wait for that reload instead
    ///     of reloading itself: a reload the dispatcher started earlier reads the zone before the
    ///     transfer key is bound (<see cref="TsigTransferHost.AddKeyAsync" />) and can store that
    ///     older copy after the key's own reload, which makes the transfer answer Refused.
    /// </summary>
    private static async Task WaitForCachedSerialAsync(TsigTransferHost host, uint serial)
    {
        AuthoritativeZoneCache cache = host.Services.GetRequiredService<AuthoritativeZoneCache>();
        DnsName apex = DnsName.Parse(TsigTransferHost.ZoneName);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));

        while (true)
        {
            RecordEntity soa = cache
                .FindZone(apex)
                ?.GetRecords(apex, DnsResourceRecordTypeEnum.Soa)
                .SingleOrDefault();

            if (
                soa is not null
                && RecordEntityCodec.DecodeTypedData(soa) is SoaRecordData data
                && data.Serial == serial
            )
                return;

            Assert.False(
                timeout.IsCancellationRequested,
                $"The cache never reached serial {serial}."
            );
            await Task.Delay(10);
        }
    }

    /// <summary>
    ///     This method builds an SOA for the hosted zone with the host's fixed names and the given
    ///     serial, refresh and minimum.
    /// </summary>
    private static SoaRecordData Soa(uint serial, uint refresh, uint minimum) =>
        new(
            DnsName.Parse($"ns1.{TsigTransferHost.ZoneName}"),
            DnsName.Parse($"admin.{TsigTransferHost.ZoneName}"),
            serial,
            refresh,
            900,
            604800,
            minimum
        );

    /// <summary>
    ///     This method builds an unsaved A record <c>hostN</c> for the hosted zone.
    /// </summary>
    private static RecordEntity ARecord(Guid zoneId, byte number) =>
        new()
        {
            Id = Guid.NewGuid(),
            ZoneId = zoneId,
            Name = $"host{number}.{TsigTransferHost.ZoneName}",
            Type = DnsResourceRecordTypeEnum.A,
            Class = DnsClassEnum.Internet,
            TimeToLiveSeconds = 3600,
            DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                new ARecordData(new System.Net.IPAddress([192, 0, 2, number]))
            ),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

    /// <summary>
    ///     This method returns the owner name of the hosted zone's <c>hostN</c> record.
    /// </summary>
    private static DnsName HostName(int number) =>
        DnsName.Parse($"host{number}.{TsigTransferHost.ZoneName}");

    /// <summary>
    ///     This method returns the identifier of the hosted zone.
    /// </summary>
    private static async Task<Guid> ZoneIdAsync(TsigTransferHost host)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        return (
            await scope
                .ServiceProvider.GetRequiredService<IZoneRepository>()
                .FindByNameAsync(TsigTransferHost.ZoneName)
        ).Id;
    }

    /// <summary>
    ///     This method loads the one stored record of a type at a name in the hosted zone.
    /// </summary>
    private static async Task<RecordEntity> FindAsync(
        TsigTransferHost host,
        Guid zoneId,
        string name,
        DnsResourceRecordTypeEnum type = DnsResourceRecordTypeEnum.A
    )
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
        return (
            await scope
                .ServiceProvider.GetRequiredService<IRecordRepository>()
                .FindByNameAsync(zoneId, name, type)
        ).Single();
    }

    /// <summary>
    ///     This method decodes the data of an SOA record from a transfer answer.
    /// </summary>
    private static SoaRecordData SoaOf(DnsResourceRecord record) =>
        Assert.IsType<SoaRecordData>(
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.Soa, record.Data)
        );

    /// <summary>
    ///     This method asserts a full-transfer shape: the current SOA first, then the zone's other
    ///     records rather than an older SOA.
    /// </summary>
    private static void AssertFullAnswer(List<DnsResourceRecord> records)
    {
        Assert.True(records.Count > 1, "A full answer carries more than the SOA.");
        Assert.Equal(DnsResourceRecordTypeEnum.Soa, records[0].Type);
        Assert.NotEqual(DnsResourceRecordTypeEnum.Soa, records[1].Type);
    }

    /// <summary>
    ///     This method runs a signed IXFR with a client SOA of <paramref name="clientSerial" />
    ///     against the host and returns every record of every message, in order.
    /// </summary>
    private static async Task<List<DnsResourceRecord>> RunIxfrAsync(
        TsigTransferHost host,
        uint clientSerial
    )
    {
        string key = await host.AddKeyAsync();
        (DnsMessage request, byte[] raw, int offset) = await host.SignedIxfrQueryAsync(
            clientSerial,
            key
        );

        List<DnsMessage> messages = await host.TransferIxfrAsync(request, raw, offset);

        Assert.All(
            messages,
            message => Assert.Equal(DnsResponseCodeEnum.NoError, message.ResponseCode)
        );
        return [.. messages.SelectMany(message => message.Answers)];
    }
}
