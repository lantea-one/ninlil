using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class is a direct regression test for the real DNS engine's wiring: sending a
///     real UDP query to this test host's own real Plain DNS listener (a genuine
///     <see cref="Socket" />-based background service, not something that only exists through
///     <c>TestServer</c>'s in-memory HTTP abstraction) and confirming a well-formed answer comes
///     back through the real, fully-wired <c>DnsQueryDispatcher</c> → <c>Server.Authoritative</c>
///     → <c>Server.Forwarding</c> → <c>Server.Recursive</c> chain -- REFUSED is the only correct
///     answer here, since this test host hosts no zones and has neither forwarding nor
///     recursion enabled, but a REFUSED that actually arrives, with the right question echoed
///     back, proves every piece of that chain (registered here for the first time ever outside a
///     unit test fixture) is wired correctly end to end.
/// </summary>
[Collection(WebTestCollection.Name)]
public class DnsWiringTests(NinlilDnsWebApplicationFactory factory)
{
    [Fact]
    public async Task ARealUdpQueryAgainstTheSharedHostReceivesAWellFormedRefusedAnswer()
    {
        // Accessing the factory's own Server property is what actually triggers the host (and
        // therefore every IHostedService it owns, including the Plain DNS listener) to start,
        // the same way calling CreateClient() already does for the REST API's own tests.
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

        // A real, previously-latent bug: a
        // REFUSED/error response built via a fresh DnsMessage object initializer rather than
        // derived from the request silently came back with an empty question section
        // (QDCOUNT=0) on the real wire, even though this test's own doc comment above always
        // claimed otherwise. Asserted here, over a real UDP socket round trip, rather than only
        // in a narrower unit test, since the earlier bug was only ever discoverable this way --
        // an in-process assertion against the DnsMessage object graph alone would not have caught
        // the same regression this test's own missing assertion once let through.
        Assert.Single(response.Questions);
        Assert.Equal(query.Questions[0].Name, response.Questions[0].Name);
        Assert.Equal(query.Questions[0].Type, response.Questions[0].Type);
        Assert.Equal(query.Questions[0].Class, response.Questions[0].Class);
    }
}
