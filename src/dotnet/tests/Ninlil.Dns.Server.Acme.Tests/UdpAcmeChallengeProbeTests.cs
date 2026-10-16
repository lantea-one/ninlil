using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Tests;

/// <summary>
///     This class tests <see cref="UdpAcmeChallengeProbe" /> against a fake name server on a
///     loopback UDP socket bound to port 0. The probe reaches it through its internal port and
///     timeout overrides (200 ms), so a reply that never matches ends the test quickly.
/// </summary>
public sealed class UdpAcmeChallengeProbeTests
{
    private static readonly DnsName Name = DnsName.Parse("_acme-challenge.example.com");

    /// <summary>
    ///     This enum is what the fake server sends back for one query.
    /// </summary>
    private enum Reply
    {
        /// <summary>The query's ID and question with the TXT value "v1".</summary>
        ValueV1,

        /// <summary>A datagram with another ID, then nothing.</summary>
        WrongIdOnly,

        /// <summary>A datagram with another question, then nothing.</summary>
        WrongQuestionOnly,

        /// <summary>A datagram with another ID, then the matching reply with "v1".</summary>
        WrongIdThenValueV1,
    }

    /// <summary>
    ///     This method answers the first query that reaches a fake server as <paramref name="reply" />
    ///     says and returns whether the probe confirmed <paramref name="asked" />.
    /// </summary>
    private static async Task<bool> ProbeAsync(Reply reply, string asked)
    {
        using Socket server = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        server.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        int port = ((IPEndPoint)server.LocalEndPoint).Port;

        Task serve = Task.Run(async () =>
        {
            byte[] buffer = new byte[4096];
            SocketReceiveFromResult received = await server.ReceiveFromAsync(
                buffer,
                new IPEndPoint(IPAddress.Any, 0)
            );
            DnsMessage query = DnsMessageReader.Read(buffer.AsSpan(0, received.ReceivedBytes));

            DnsMessage Build(ushort id, DnsName name) =>
                new()
                {
                    Id = id,
                    IsResponse = true,
                    IsAuthoritativeAnswer = true,
                    Questions =
                    [
                        new DnsQuestion(name, DnsResourceRecordTypeEnum.Txt, DnsClassEnum.Internet),
                    ],
                    Answers =
                    [
                        new DnsResourceRecord(
                            name,
                            DnsResourceRecordTypeEnum.Txt,
                            DnsClassEnum.Internet,
                            60,
                            DnsRecordDataCodecRegistry.Encode(
                                DnsResourceRecordTypeEnum.Txt,
                                new TxtRecordData(["v1"])
                            )
                        ),
                    ],
                };

            async Task SendAsync(DnsMessage message) =>
                await server.SendToAsync(DnsMessageWriter.Write(message), received.RemoteEndPoint);

            ushort otherId = (ushort)(query.Id + 1);

            switch (reply)
            {
                case Reply.ValueV1:
                    await SendAsync(Build(query.Id, Name));
                    break;
                case Reply.WrongIdOnly:
                    await SendAsync(Build(otherId, Name));
                    break;
                case Reply.WrongQuestionOnly:
                    await SendAsync(Build(query.Id, DnsName.Parse("_acme-challenge.other.test")));
                    break;
                case Reply.WrongIdThenValueV1:
                    await SendAsync(Build(otherId, Name));
                    await SendAsync(Build(query.Id, Name));
                    break;
            }
        });

        UdpAcmeChallengeProbe probe = new(port, TimeSpan.FromMilliseconds(200));
        bool result = await probe.ServesValueAsync(
            IPAddress.Loopback,
            Name,
            asked,
            CancellationToken.None
        );
        await serve;

        return result;
    }

    [Fact]
    public async Task ServesValue_WhenTheServerAnswersTheValue_ReturnsTrue() =>
        Assert.True(await ProbeAsync(Reply.ValueV1, "v1"));

    [Fact]
    public async Task ServesValue_WhenTheServerAnswersAnotherValue_ReturnsFalse() =>
        Assert.False(await ProbeAsync(Reply.ValueV1, "v2"));

    [Fact]
    public async Task ServesValue_WhenOnlyAReplyWithAnotherIdArrives_IgnoresItUntilTheTimeout() =>
        Assert.False(await ProbeAsync(Reply.WrongIdOnly, "v1"));

    [Fact]
    public async Task ServesValue_WhenOnlyAReplyWithAnotherQuestionArrives_IgnoresItUntilTheTimeout() =>
        Assert.False(await ProbeAsync(Reply.WrongQuestionOnly, "v1"));

    [Fact]
    public async Task ServesValue_WhenAReplyWithAnotherIdPrecedesTheRealOne_ReturnsTrue() =>
        Assert.True(await ProbeAsync(Reply.WrongIdThenValueV1, "v1"));
}
