using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Forwarding.Clients;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.ClientProtocols.Tests;

/// <summary>
///     This class tests <see cref="TlsForwarderClient" /> end to end, including certificate
///     validation against the operating system's trust store, by querying Cloudflare's public
///     DoT resolver over the internet. A local listener cannot be used: Ninlil's DoT listener
///     presents a self-signed certificate by default, and <see cref="TlsForwarderClient" /> has no way to
///     skip validation. Without outbound internet access the test fails, which then says nothing
///     about the code.
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
