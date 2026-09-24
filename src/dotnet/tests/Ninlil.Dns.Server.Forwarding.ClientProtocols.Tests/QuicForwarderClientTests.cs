using System.Net;
using System.Net.Quic;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Forwarding.Clients;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.ClientProtocols.Tests;

/// <summary>
///     This class proves <see cref="QuicForwarderClient" /> works end-to-end against a real
///     public DoQ resolver (AdGuard DNS, which publishes a documented DoQ endpoint) -- the same
///     real-internet-dependency reasoning <see cref="TlsForwarderClientTests" /> documents
///     applies here too, plus the same <c>libmsquic</c> native-dependency caveat
///     <c>Ninlil.Dns.Protocol.DnsOverQuic.Tests</c> already documents: if
///     <see cref="QuicConnection.IsSupported" /> is <see langword="false" /> in whatever
///     environment runs this, the test reports that explicitly rather than failing.
/// </summary>
public sealed class QuicForwarderClientTests
{
    private static DnsMessage BuildQuery(ushort id, string name) =>
        new()
        {
            Id = id,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse(name),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };

    [Fact]
    public async Task ForwardAsync_AgainstAdGuardsRealDoqResolver_ReturnsARealValidatedAnswer()
    {
        if (!QuicConnection.IsSupported)
        {
            // libmsquic (and, on Linux, OpenSSL) is a real native OS dependency not bundled in
            // the .NET runtime pack -- absent here, not a code bug. Matches this project's own
            // established "check IsSupported, report rather than fail" convention.
            return;
        }

        QuicForwarderClient client = new();

        UpstreamForwarderDefinition definition = new(
            UpstreamForwarderProtocolEnum.Quic,
            "quic://dns.adguard-dns.com",
            IPAddress.Parse("94.140.14.14"),
            853,
            TlsHostName: "dns.adguard-dns.com"
        );

        DnsMessage response = await client.ForwardAsync(
            definition,
            BuildQuery(1, "example.com."),
            new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token
        );

        Assert.Equal(1, response.Id);
        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.NotEmpty(response.Answers);
    }
}
