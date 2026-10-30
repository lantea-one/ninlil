using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.Transport;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;
using Ninlil.Dns.Server.Recursive.Transport;

namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests <see cref="UpstreamDnsClient" /> over real loopback sockets: a test server
///     on an unprivileged port answers each query with stray datagrams before the real reply, and
///     the client, using a pooled unconnected socket, must skip the strays (RFC 5452 §9.1).
/// </summary>
public class UpstreamDnsClientTests
{
    private static readonly DnsName QueriedName = DnsName.Parse("www.example.com");
    private static readonly IPAddress AnswerAddress = IPAddress.Parse("192.0.2.10");

    /// <summary>
    ///     This method runs a one-query UDP server: it reads the query, sends every stray reply
    ///     <paramref name="buildStrays" /> builds from it, then the matching answer.
    /// </summary>
    private static async Task ServeOneQueryAsync(
        UdpClient server,
        Func<DnsMessage, IEnumerable<DnsMessage>> buildStrays
    )
    {
        UdpReceiveResult received = await server.ReceiveAsync();
        DnsMessage query = DnsMessageReader.Read(received.Buffer);

        foreach (DnsMessage stray in buildStrays(query))
            await server.SendAsync(DnsMessageWriter.Write(stray), received.RemoteEndPoint);

        DnsMessage answer = BuildReply(query.Id, query.Questions[0]);
        answer.Answers.Add(
            new DnsResourceRecord(
                QueriedName,
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet,
                300,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.A,
                    new ARecordData(AnswerAddress)
                )
            )
        );
        await server.SendAsync(DnsMessageWriter.Write(answer), received.RemoteEndPoint);
    }

    private static DnsMessage BuildReply(ushort id, DnsQuestion question) =>
        new()
        {
            Id = id,
            IsResponse = true,
            ResponseCode = DnsResponseCodeEnum.NoError,
            Questions = [question],
        };

    private static async Task<DnsMessage> QueryThroughPoolAsync(
        Func<DnsMessage, IEnumerable<DnsMessage>> buildStrays
    )
    {
        using UdpClient server = new(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        Task serving = ServeOneQueryAsync(server, buildStrays);

        using OutboundUdpSocketPool pool = new(
            new StaticOptionsMonitor<OutboundQueryOptions>(new OutboundQueryOptions()),
            NullLogger<OutboundUdpSocketPool>.Instance
        );
        UpstreamDnsClient client = new(
            new StaticOptionsMonitor<RecursiveServerOptions>(new RecursiveServerOptions()),
            pool,
            serverPort: port
        );

        DnsMessage response = await client.QueryAsync(
            IPAddress.Loopback,
            QueriedName,
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            false
        );
        await serving;

        return response;
    }

    [Fact]
    public async Task QueryAsyncIgnoresALateDatagramWithTheWrongMessageId()
    {
        DnsMessage response = await QueryThroughPoolAsync(query =>
            [BuildReply((ushort)(query.Id + 1), query.Questions[0])]
        );

        Assert.NotNull(response);
        Assert.Single(response.Answers);
    }

    [Fact]
    public async Task QueryAsyncIgnoresADatagramWithTheRightIdButAnotherQuestion()
    {
        DnsMessage response = await QueryThroughPoolAsync(query =>
            [
                BuildReply(
                    query.Id,
                    new DnsQuestion(
                        DnsName.Parse("other.example.com"),
                        DnsResourceRecordTypeEnum.A,
                        DnsClassEnum.Internet
                    )
                ),
            ]
        );

        Assert.NotNull(response);
        Assert.Equal(QueriedName, response.Questions[0].Name);
        Assert.Single(response.Answers);
    }
}
