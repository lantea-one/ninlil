using System.Net;
using System.Net.Http.Headers;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Forwarding.Clients;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This class tests <see cref="HttpsForwarderClient" />'s message-ID and reply-matching rules
///     through its internal constructor, with a fake <see cref="HttpMessageHandler" /> in place of
///     a network: no sockets, no certificates.
/// </summary>
public class HttpsForwarderClientTests
{
    /// <summary>
    ///     This class is a fake HTTP handler that records the request body and answers with a
    ///     reply built by a callback from the decoded query.
    /// </summary>
    private sealed class FakeDohHandler(Func<DnsMessage, DnsMessage> buildReply)
        : HttpMessageHandler
    {
        /// <summary>
        ///     This property contains the DNS message of the latest request body.
        /// </summary>
        public DnsMessage LastQuery { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken stoppingToken
        )
        {
            byte[] body = await request.Content.ReadAsByteArrayAsync(stoppingToken);
            LastQuery = DnsMessageReader.Read(body);

            ByteArrayContent content = new(DnsMessageWriter.Write(buildReply(LastQuery)));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }

    private static readonly UpstreamForwarderDefinition Definition = new(
        UpstreamForwarderProtocolEnum.Https,
        "https://doh.test/dns-query",
        IPAddress.Loopback,
        443,
        DohUri: new Uri("https://doh.test/dns-query")
    );

    private static DnsMessage Query(string name)
    {
        DnsMessage message = new()
        {
            Id = 0x1234,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
        };
        message.Questions.Add(
            new DnsQuestion(DnsName.Parse(name), DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet)
        );
        return message;
    }

    /// <summary>
    ///     This method builds a reply that echoes the query's question and ID, as a DoH server
    ///     does.
    /// </summary>
    private static DnsMessage Echo(DnsMessage query)
    {
        DnsMessage reply = new()
        {
            Id = query.Id,
            IsResponse = true,
            RecursionDesired = true,
            RecursionAvailable = true,
        };
        reply.Questions.AddRange(query.Questions);
        return reply;
    }

    [Fact]
    public async Task ForwardAsync_Always_SendsMessageIdZeroAndRestoresTheClientsIdOnTheReply()
    {
        FakeDohHandler handler = new(Echo);
        HttpsForwarderClient client = new(handler);
        DnsMessage query = Query("www.example.com");

        DnsMessage reply = await client.ForwardAsync(Definition, query);

        Assert.Equal(0, handler.LastQuery.Id);
        Assert.Equal(0x1234, query.Id);
        Assert.Equal(0x1234, reply.Id);
    }

    [Fact]
    public async Task ForwardAsync_ForAReplyToAnotherQuestion_Throws()
    {
        FakeDohHandler handler = new(query =>
        {
            DnsMessage reply = Echo(query);
            reply.Questions.Clear();
            reply.Questions.AddRange(Query("evil.example").Questions);
            return reply;
        });
        HttpsForwarderClient client = new(handler);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.ForwardAsync(Definition, Query("www.example.com"))
        );
    }

    [Fact]
    public async Task ForwardAsync_ForAReplyWithTheQuestionInAnotherCase_Succeeds()
    {
        FakeDohHandler handler = new(query =>
        {
            DnsMessage reply = Echo(query);
            reply.Questions.Clear();
            reply.Questions.AddRange(Query("WWW.EXAMPLE.COM").Questions);
            return reply;
        });
        HttpsForwarderClient client = new(handler);

        DnsMessage reply = await client.ForwardAsync(Definition, Query("www.example.com"));

        Assert.True(reply.IsResponse);
    }
}
