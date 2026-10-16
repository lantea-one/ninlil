using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This class is the default <see cref="IAcmeChallengeProbe" />: one TXT query over UDP to
///     port 53 with a random message ID. A reply counts only when it comes from the queried
///     address, repeats the ID and the question (RFC 5452 §9.1) and holds the value, so a stray
///     datagram cannot confirm a server. Datagrams that do not match are ignored until the probe
///     timeout ends the wait. The queries are plain, non-recursive questions to a public name
///     server, exactly what the CA sends.
/// </summary>
public sealed class UdpAcmeChallengeProbe : IAcmeChallengeProbe
{
    /// <summary>
    ///     This field contains the port the name servers listen on. Tests point it at a fake server.
    /// </summary>
    private readonly int _port;

    /// <summary>
    ///     This field contains how long one probe waits for an acceptable reply.
    /// </summary>
    private readonly TimeSpan _timeout;

    /// <summary>
    ///     This constructor probes port 53 and waits two seconds for a reply.
    /// </summary>
    public UdpAcmeChallengeProbe()
        : this(53, TimeSpan.FromSeconds(2)) { }

    /// <summary>
    ///     This constructor is the test seam: a test binds a fake name server to port 0 and passes
    ///     its port and a short timeout here. The dependency injection container ignores it
    ///     because it is not public.
    /// </summary>
    /// <param name="port">The UDP port to send the query to.</param>
    /// <param name="timeout">How long to wait for an acceptable reply.</param>
    internal UdpAcmeChallengeProbe(int port, TimeSpan timeout)
    {
        _port = port;
        _timeout = timeout;
    }

    /// <inheritdoc />
    public async Task<bool> ServesValueAsync(
        IPAddress server,
        DnsName name,
        string dnsTxtValue,
        CancellationToken stoppingToken
    )
    {
        DnsQuestion question = new(name, DnsResourceRecordTypeEnum.Txt, DnsClassEnum.Internet);
        DnsMessage query = new()
        {
            Id = (ushort)RandomNumberGenerator.GetInt32(ushort.MaxValue + 1),
            RecursionDesired = false,
            Questions = [question],
        };

        IPEndPoint endpoint = new(server, _port);
        using Socket socket = new(server.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutSource.CancelAfter(_timeout);

        try
        {
            await socket.SendToAsync(DnsMessageWriter.Write(query), endpoint, timeoutSource.Token);

            byte[] buffer = new byte[4096];

            while (true)
            {
                SocketReceiveFromResult result = await socket.ReceiveFromAsync(
                    buffer,
                    endpoint,
                    timeoutSource.Token
                );

                if (!Equals(result.RemoteEndPoint, endpoint))
                    continue;

                DnsMessage reply;

                try
                {
                    reply = DnsMessageReader.Read(buffer.AsSpan(0, result.ReceivedBytes));
                }
                catch (FormatException)
                {
                    continue;
                }

                if (
                    reply.Id != query.Id
                    || reply.Questions.Count != 1
                    || reply.Questions[0] != question
                )
                    continue;

                return reply.ResponseCode == DnsResponseCodeEnum.NoError
                    && reply.Answers.Any(answer => IsValue(answer, name, dnsTxtValue));
            }
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // The probe timeout, not the caller: the server did not answer in time.
            return false;
        }
    }

    /// <summary>
    ///     This method reports whether <paramref name="answer" /> is a TXT record at
    ///     <paramref name="name" /> holding exactly the one string <paramref name="value" />, the
    ///     form the responder publishes.
    /// </summary>
    private static bool IsValue(DnsResourceRecord answer, DnsName name, string value) =>
        answer.Type == DnsResourceRecordTypeEnum.Txt
        && answer.Name.Equals(name)
        && DnsRecordDataCodecRegistry.Decode(answer.Type, answer.Data)
            is TxtRecordData { Values.Count: 1 } txt
        && string.Equals(txt.Values[0], value, StringComparison.Ordinal);
}
