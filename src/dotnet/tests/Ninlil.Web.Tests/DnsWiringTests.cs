using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class checks that the DNS engine is wired up in the composed host. It sends a UDP
///     query from a <see cref="Socket" /> to the shared host's Plain DNS listener
///     (<see cref="NinlilDnsWebApplicationFactory.PlainDnsPort" />), which listens on a real
///     socket rather than through <c>TestServer</c>, and checks that an answer comes back through
///     <c>DnsQueryDispatcher</c> and the authoritative, forwarding and recursive stages. The test
///     host has no zones and neither forwarding nor recursion enabled, so REFUSED is the correct
///     answer, but it must arrive with the matching ID and the question echoed back.
/// </summary>
[Collection(WebTestCollection.Name)]
public class DnsWiringTests(NinlilDnsWebApplicationFactory factory)
{
    [Fact]
    public async Task ARealUdpQueryAgainstTheSharedHostReceivesAWellFormedRefusedAnswer()
    {
        // Reading Server starts the host and its hosted services, including the Plain DNS
        // listener, as CreateClient() does for the API tests.
        _ = factory.Server;

        DnsMessage query = new()
        {
            Id = 4242,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("dns-wiring-test.example."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };
        byte[] queryBytes = DnsMessageWriter.Write(query);

        using UdpClient client = new();
        client.Client.ReceiveTimeout = 5000;
        await client.SendAsync(queryBytes, queryBytes.Length, "127.0.0.1", factory.PlainDnsPort);

        UdpReceiveResult result = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(10));
        DnsMessage response = DnsMessageReader.Read(result.Buffer);

        Assert.Equal(query.Id, response.Id);
        Assert.Equal(DnsResponseCodeEnum.Refused, response.ResponseCode);

        // An error response must echo the question (QDCOUNT=1). A REFUSED built as a new
        // DnsMessage instead of from the request has an empty question section, which only shows
        // on the wire, so it is checked here over UDP rather than on the object graph.
        Assert.Single(response.Questions);
        Assert.Equal(query.Questions[0].Name, response.Questions[0].Name);
        Assert.Equal(query.Questions[0].Type, response.Questions[0].Type);
        Assert.Equal(query.Questions[0].Class, response.Questions[0].Class);
    }
}
