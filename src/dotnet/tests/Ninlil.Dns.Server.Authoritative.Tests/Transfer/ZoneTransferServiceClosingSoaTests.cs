using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Transfer;

/// <summary>
///     This class tests that <see cref="ZoneTransferService" /> ends every AXFR and every IXFR
///     answer longer than the single "current" SOA with the zone's SOA, in one message or many (RFC
///     5936 §2.2, RFC 1995 §4), and that <see cref="ZoneTransferResponseAssembler" /> reads what it
///     produces. It uses a <see cref="TsigTransferHost" /> (SQLite, no sockets; the zone is at
///     serial 100 with five A records) and reads the yielded messages directly.
/// </summary>
public class ZoneTransferServiceClosingSoaTests
{
    [Fact]
    public async Task CreateAxfrResponseAsync_InOneMessage_EndsWithTheLeadingSoa()
    {
        await using TsigTransferHost host = await TsigTransferHost.CreateAsync(100);

        List<DnsMessage> messages = await RunAxfrAsync(host);

        DnsMessage message = Assert.Single(messages);
        Assert.Equal(7, message.Answers.Count);
        Assert.Equal(DnsResourceRecordTypeEnum.Soa, message.Answers[0].Type);
        Assert.Equal(DnsResourceRecordTypeEnum.Soa, message.Answers[^1].Type);
        Assert.Equal(message.Answers[0].Data, message.Answers[^1].Data);
    }

    [Fact]
    public async Task CreateAxfrResponseAsync_OverSeveralMessages_EndsWithTheSoaOnlyInTheLastMessage()
    {
        await using TsigTransferHost host = await TsigTransferHost.CreateAsync(2);

        List<DnsMessage> messages = await RunAxfrAsync(host);

        Assert.True(messages.Count > 1);
        Assert.Equal(DnsResourceRecordTypeEnum.Soa, messages[0].Answers[0].Type);
        Assert.Equal(DnsResourceRecordTypeEnum.Soa, messages[^1].Answers[^1].Type);
        Assert.Equal(2, messages.SelectMany(message => message.Answers).Count(IsSoa));
        Assert.Single(messages[0].Answers, IsSoa);
    }

    [Fact]
    public async Task CreateIxfrResponseAsync_AnIncrementalAnswerInOneMessage_EndsWithTheCurrentSoa()
    {
        await using TsigTransferHost host = await TsigTransferHost.CreateAsync(100);
        await host.AddJournalEntryAsync(90, 100);

        List<DnsMessage> messages = await RunIxfrAsync(host, 90);

        // Current SOA, the step's old SOA, the step's new SOA, then the closing SOA.
        DnsMessage message = Assert.Single(messages);
        Assert.Equal(4, message.Answers.Count);
        Assert.All(
            message.Answers,
            record => Assert.Equal(DnsResourceRecordTypeEnum.Soa, record.Type)
        );

        // The pull side reads the answer back as one empty step ending at the current serial.
        ZoneTransferResponseAssembler assembler = new(90);
        Assert.Null(assembler.AddMessage(message.Answers));
        Assert.True(assembler.IsComplete);
        ZoneTransferPullResult result = assembler.BuildResult();
        Assert.True(result.IsIncremental);
        ZoneTransferDiffStep step = Assert.Single(result.DiffSteps);
        Assert.Equal(90u, step.OldSerial);
        Assert.Equal(100u, step.NewSerial);
    }

    [Fact]
    public async Task CreateIxfrResponseAsync_AFullFallbackInOneMessage_EndsWithTheCurrentSoa()
    {
        await using TsigTransferHost host = await TsigTransferHost.CreateAsync(100);

        List<DnsMessage> messages = await RunIxfrAsync(host, 50);

        DnsMessage message = Assert.Single(messages);
        Assert.Equal(7, message.Answers.Count);
        Assert.Equal(message.Answers[0].Data, message.Answers[^1].Data);

        ZoneTransferResponseAssembler assembler = new(50);
        Assert.Null(assembler.AddMessage(message.Answers));
        Assert.True(assembler.IsComplete);
        Assert.Equal(6, assembler.BuildResult().FullRecordSet.Count);
    }

    [Fact]
    public async Task CreateIxfrResponseAsync_AFullFallbackOverSeveralMessages_EndsWithTheSoaInTheLastMessage()
    {
        await using TsigTransferHost host = await TsigTransferHost.CreateAsync(2);

        List<DnsMessage> messages = await RunIxfrAsync(host, 50);

        Assert.True(messages.Count > 1);
        Assert.Equal(DnsResourceRecordTypeEnum.Soa, messages[^1].Answers[^1].Type);
        Assert.Equal(2, messages.SelectMany(message => message.Answers).Count(IsSoa));
    }

    [Fact]
    public async Task CreateIxfrResponseAsync_AClientAtTheCurrentSerial_GetsExactlyOneSoa()
    {
        await using TsigTransferHost host = await TsigTransferHost.CreateAsync(100);

        List<DnsMessage> messages = await RunIxfrAsync(host, 100);

        DnsMessage message = Assert.Single(messages);
        Assert.Single(message.Answers);
    }

    /// <summary>
    ///     This method tells whether a record is an SOA.
    /// </summary>
    private static bool IsSoa(DnsResourceRecord record) =>
        record.Type == DnsResourceRecordTypeEnum.Soa;

    /// <summary>
    ///     This method runs a signed AXFR against the host and returns the messages.
    /// </summary>
    private static async Task<List<DnsMessage>> RunAxfrAsync(TsigTransferHost host)
    {
        string key = await host.AddKeyAsync();
        (DnsMessage request, byte[] raw, int offset) = await host.SignedQueryAsync(
            TsigTransferHost.ZoneName,
            DnsResourceRecordTypeEnum.Axfr,
            key
        );

        List<DnsMessage> messages = await host.TransferAsync(request, raw, offset);

        Assert.All(
            messages,
            message => Assert.Equal(DnsResponseCodeEnum.NoError, message.ResponseCode)
        );
        return messages;
    }

    /// <summary>
    ///     This method runs a signed IXFR with a client SOA of <paramref name="clientSerial" />
    ///     against the host and returns the messages.
    /// </summary>
    private static async Task<List<DnsMessage>> RunIxfrAsync(
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
        return messages;
    }
}
