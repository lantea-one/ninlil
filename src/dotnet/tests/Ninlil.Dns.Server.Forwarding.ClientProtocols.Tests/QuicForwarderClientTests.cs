using System.Net;
using System.Net.Quic;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Forwarding.Clients;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.ClientProtocols.Tests;

/// <summary>
///     This class tests <see cref="QuicForwarderClient" /> end to end against a public DoQ
///     resolver over the internet, for the reason <see cref="TlsForwarderClientTests" />
///     gives. It needs internet access and <c>libmsquic</c>; when
///     <see cref="QuicConnection.IsSupported" /> is <see langword="false" />, the test returns
///     early and passes.
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
    public async Task ForwardAsync_AgainstAPublicDoqResolver_ReturnsAValidatedAnswer()
    {
        if (!QuicConnection.IsSupported)
        {
            // libmsquic (and, on Linux, OpenSSL) is a native dependency outside the .NET runtime,
            // so its absence is an environment gap, not a code bug.
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
