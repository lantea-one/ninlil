using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Forwarding.Clients;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.ClientProtocols.Tests;

/// <summary>
///     This class proves <see cref="TlsForwarderClient" /> works end-to-end, including real
///     certificate validation against the OS trust store. Unlike <see cref="PlainForwarderClientTests" />,
///     this cannot point at a local listener: this project's own DoT <em>listener</em>
///     deliberately presents a self-signed certificate to inbound clients, and
///     <see cref="TlsForwarderClient" /> deliberately never exposes a way to bypass certificate
///     validation (a real security boundary for an outbound connection to a third party) -- so
///     the only way to prove validation genuinely succeeds against a real, trusted certificate
///     chain is to query a real public DoT resolver over the internet. This is a real, live
///     network dependency, the same category of dependency this project's own first genuinely
///     over-the-wire smoke test already accepted for real root/TLD servers -- if this
///     environment (or CI) ever loses outbound internet access, this specific test will fail for
///     that reason, not a code regression.
/// </summary>
public sealed class TlsForwarderClientTests
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
    public async Task ForwardAsync_AgainstCloudflaresRealDotResolver_ReturnsARealValidatedAnswer()
    {
        TlsForwarderClient client = new();

        UpstreamForwarderDefinition definition = new(
            UpstreamForwarderProtocolEnum.Tls,
            "tls://1.1.1.1",
            IPAddress.Parse("1.1.1.1"),
            853,
            TlsHostName: "cloudflare-dns.com"
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
