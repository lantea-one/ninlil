using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Tsig;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Transfer;

/// <summary>
///     This class tests how <see cref="ZoneTransferService" /> answers signed transfer requests,
///     with the real TSIG service and zone cache on a shared <see cref="TsigTransferHost" />
///     (SQLite, no sockets): a failed signature gets a NOTAUTH whose TSIG record carries the error,
///     and a refusal after a good signature is itself signed. Replies are re-read from their wire
///     bytes so the TSIG offsets are the ones a receiver sees.
/// </summary>
public class ZoneTransferServiceTsigTests(TsigTransferFixture fixture)
    : IClassFixture<TsigTransferFixture>
{
    private readonly TsigTransferHost _host = fixture.Host;

    [Fact]
    public async Task CreateAxfrResponseAsync_ABadMac_YieldsOneNotAuthWithBadSigInTheTsigRecord()
    {
        string key = await _host.AddKeyAsync();
        (DnsMessage request, byte[] raw, int offset) = await _host.SignedQueryAsync(
            TsigTransferHost.ZoneName,
            DnsResourceRecordTypeEnum.Axfr,
            key
        );

        // Flipping the case of the first question letter keeps the message well formed but
        // changes the bytes the MAC covers.
        raw[13] ^= 0x20;
        request = DnsMessageReader.Read(raw, out offset);

        List<DnsMessage> messages = await _host.TransferAsync(request, raw, offset);

        DnsMessage reply = Assert.Single(messages);
        Assert.Equal(DnsResponseCodeEnum.NotAuthoritative, reply.ResponseCode);
        Assert.NotNull(reply.Tsig);
        Assert.Equal(DnsResponseCodeEnum.BadVersionOrBadSignature, reply.Tsig.Error);
        Assert.Empty(reply.Tsig.Mac);
    }

    [Fact]
    public async Task CreateAxfrResponseAsync_AClusterKey_YieldsNotAuthWithBadKeyInTheTsigRecord()
    {
        string key = await _host.AddKeyAsync(isClusterKey: true);

        // A cluster key cannot sign, so the request carries a TSIG record by hand. The key check
        // comes before the MAC check, so the MAC value does not matter.
        DnsMessage unsigned = new()
        {
            Id = 7,
            Opcode = DnsOpcodeEnum.Query,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse(TsigTransferHost.ZoneName),
                    DnsResourceRecordTypeEnum.Axfr,
                    DnsClassEnum.Internet
                ),
            },
            Tsig = new DnsTsigOptions(
                DnsName.Parse(key),
                DnsTsigOptions.HmacSha256AlgorithmName,
                (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                300,
                new byte[32],
                7,
                DnsResponseCodeEnum.NoError,
                []
            ),
        };
        byte[] raw = DnsMessageWriter.Write(unsigned);
        DnsMessage request = DnsMessageReader.Read(raw, out int offset);

        List<DnsMessage> messages = await _host.TransferAsync(request, raw, offset);

        DnsMessage reply = Assert.Single(messages);
        Assert.Equal(DnsResponseCodeEnum.NotAuthoritative, reply.ResponseCode);
        Assert.Equal(DnsResponseCodeEnum.BadKey, reply.Tsig.Error);
    }

    [Fact]
    public async Task CreateAxfrResponseAsync_AValidKeyForAnUnhostedZone_YieldsASignedRefused()
    {
        string key = await _host.AddKeyAsync();
        (DnsMessage request, byte[] raw, int offset) = await _host.SignedQueryAsync(
            "not-hosted.example.org",
            DnsResourceRecordTypeEnum.Axfr,
            key
        );

        List<DnsMessage> messages = await _host.TransferAsync(request, raw, offset);

        DnsMessage reply = Assert.Single(messages);
        Assert.Equal(DnsResponseCodeEnum.Refused, reply.ResponseCode);
        Assert.NotNull(reply.Tsig);
        Assert.Equal(DnsResponseCodeEnum.NoError, reply.Tsig.Error);

        byte[] replyBytes = DnsMessageWriter.Write(reply);
        DnsMessage reparsed = DnsMessageReader.Read(replyBytes, out int replyOffset);
        TsigVerificationResult result = await _host.WithTsigAsync(tsigService =>
            tsigService.VerifyResponseAsync(
                replyBytes,
                replyOffset,
                reparsed,
                request.Tsig.Mac,
                key
            )
        );

        Assert.True(result.IsValid, $"The REFUSED reply's TSIG failed with {result.Error}.");
    }
}
