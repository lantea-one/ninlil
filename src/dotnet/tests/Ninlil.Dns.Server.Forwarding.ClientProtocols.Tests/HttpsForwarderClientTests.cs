using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Forwarding.Clients;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.ClientProtocols.Tests;

/// <summary>
///     This class tests <see cref="HttpsForwarderClient" /> end to end against Cloudflare's public
///     DoH resolver over the internet, for the reason <see cref="TlsForwarderClientTests" /> gives:
///     certificate validation is left to <see cref="System.Net.Http.HttpClient" />'s default
///     handler, so only a publicly trusted server can pass it. Without internet access the test
///     fails.
/// </summary>
public sealed class HttpsForwarderClientTests
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
    public async Task ForwardAsync_AgainstCloudflaresRealDohResolver_ReturnsARealValidatedAnswer()
    {
        HttpsForwarderClient client = new();

        UpstreamForwarderDefinition definition = new(
            UpstreamForwarderProtocolEnum.Https,
            "https://cloudflare-dns.com/dns-query",
            System.Net.IPAddress.Parse("1.1.1.1"),
            443,
            DohUri: new Uri("https://cloudflare-dns.com/dns-query")
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
