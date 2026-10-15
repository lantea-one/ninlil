using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Transfer;

/// <summary>
///     This class tests how <see cref="ZoneTransferService" /> compares the client's SOA serial
///     with the zone's under RFC 1982 §3.2 when it answers an IXFR. It uses a
///     <see cref="TsigTransferHost" /> (SQLite, no sockets) whose zone is at serial 100, and reads
///     the yielded messages directly.
/// </summary>
public class ZoneTransferServiceIxfrSerialTests(TsigTransferFixture fixture)
    : IClassFixture<TsigTransferFixture>
{
    private readonly TsigTransferHost _host = fixture.Host;

    [Fact]
    public async Task CreateIxfrResponseAsync_AClientAtTheTopOfTheSerialSpace_GetsAFullAnswer()
    {
        // The zone's serial (100) is ahead of 4294967295 once the serial has wrapped.
        List<DnsResourceRecord> records = await RunIxfrAsync(_host, 4294967295);

        AssertFullAnswer(records);
    }

    [Fact]
    public async Task CreateIxfrResponseAsync_AClientAheadOfTheZone_GetsTheCurrentSoaAlone()
    {
        List<DnsResourceRecord> records = await RunIxfrAsync(_host, 101);

        Assert.Equal(DnsResourceRecordTypeEnum.Soa, Assert.Single(records).Type);
    }

    [Fact]
    public async Task CreateIxfrResponseAsync_AClientAtTheSameSerial_GetsTheCurrentSoaAlone()
    {
        List<DnsResourceRecord> records = await RunIxfrAsync(_host, 100);

        Assert.Equal(DnsResourceRecordTypeEnum.Soa, Assert.Single(records).Type);
    }

    [Fact]
    public async Task CreateIxfrResponseAsync_AClientExactlyTwoToTheThirtyFirstAway_GetsTheCurrentSoaAlone()
    {
        // 100 + 2^31 is the undefined pair of RFC 1982 §3.2, which counts as not newer.
        List<DnsResourceRecord> records = await RunIxfrAsync(_host, 100u + 0x80000000u);

        Assert.Equal(DnsResourceRecordTypeEnum.Soa, Assert.Single(records).Type);
    }

    [Fact]
    public async Task CreateIxfrResponseAsync_AJournalChainAcrossTheWrap_FallsBackToAFullAnswer()
    {
        // This test has its own host: the journal entries it stores must not reach other tests.
        await using TsigTransferHost host = await TsigTransferHost.CreateAsync();
        await host.AddJournalEntryAsync(4294967294, 4294967295);
        await host.AddJournalEntryAsync(4294967295, 0);
        await host.AddJournalEntryAsync(0, 100);

        // The journal query compares raw serials, so the chain from 4294967294 never reaches the
        // current serial and RFC 1995 §4 allows the full answer.
        List<DnsResourceRecord> records = await RunIxfrAsync(host, 4294967294);

        AssertFullAnswer(records);
    }

    /// <summary>
    ///     This method runs an IXFR with a client SOA of <paramref name="clientSerial" /> and
    ///     returns every record of every message, in order.
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
}
