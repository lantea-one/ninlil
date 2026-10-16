using System.Buffers.Binary;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Forwarding.Clients;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This class tests <see cref="QuicForwarderClient" />'s message-ID and reply-matching rules
///     (RFC 9250 §4.2.1) against a fake DNS-over-QUIC upstream: a real <see cref="QuicListener" />
///     on a loopback port chosen by the operating system, with a throwaway self-signed
///     certificate that the client accepts through its internal constructor.
///     <para>
///         QUIC needs <c>libmsquic</c>. Every test returns early (and so reports as passed, not
///         skipped) when <see cref="QuicListener.IsSupported" /> or
///         <see cref="QuicConnection.IsSupported" /> is false.
///     </para>
/// </summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("windows")]
public class QuicForwarderClientTests
{
    private static readonly SslApplicationProtocol DoqApplicationProtocol = new("doq");

    /// <summary>
    ///     This class is a fake DoQ upstream. It accepts one connection and one stream, records the
    ///     decoded query, and answers with the reply a callback builds from it.
    /// </summary>
    private sealed class FakeDoqUpstream : IAsyncDisposable
    {
        private readonly QuicListener listener;
        private readonly X509Certificate2 certificate;
        private readonly Task serving;

        private FakeDoqUpstream(
            QuicListener listener,
            X509Certificate2 certificate,
            Func<DnsMessage, DnsMessage> buildReply
        )
        {
            this.listener = listener;
            this.certificate = certificate;
            serving = ServeOnceAsync(buildReply);
        }

        /// <summary>
        ///     This property contains the port the fake listens on.
        /// </summary>
        public int Port => listener.LocalEndPoint.Port;

        /// <summary>
        ///     This property contains the query the fake received, or <see langword="null" /> when
        ///     none has arrived.
        /// </summary>
        public DnsMessage ReceivedQuery { get; private set; }

        /// <summary>
        ///     This method starts a fake upstream on a free loopback port.
        /// </summary>
        public static async Task<FakeDoqUpstream> StartAsync(
            Func<DnsMessage, DnsMessage> buildReply
        )
        {
            // The certificate is exported and re-imported so the private key is a persisted one;
            // MsQuic cannot always use an ephemeral in-memory key.
            using RSA rsa = RSA.Create(2048);
            CertificateRequest request = new(
                "CN=localhost",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
            using X509Certificate2 ephemeral = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddMinutes(-5),
                DateTimeOffset.UtcNow.AddMinutes(30)
            );
            X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12(
                ephemeral.Export(X509ContentType.Pfx),
                null
            );

            QuicListener listener = await QuicListener.ListenAsync(
                new QuicListenerOptions
                {
                    ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
                    ApplicationProtocols = [DoqApplicationProtocol],
                    ConnectionOptionsCallback = (_, _, _) =>
                        ValueTask.FromResult(
                            new QuicServerConnectionOptions
                            {
                                DefaultCloseErrorCode = 0,
                                DefaultStreamErrorCode = 0,
                                ServerAuthenticationOptions = new SslServerAuthenticationOptions
                                {
                                    ApplicationProtocols = [DoqApplicationProtocol],
                                    ServerCertificate = certificate,
                                },
                            }
                        ),
                }
            );

            return new FakeDoqUpstream(listener, certificate, buildReply);
        }

        private async Task ServeOnceAsync(Func<DnsMessage, DnsMessage> buildReply)
        {
            try
            {
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
                await using QuicConnection connection = await listener.AcceptConnectionAsync(
                    timeout.Token
                );
                await using QuicStream stream = await connection.AcceptInboundStreamAsync(
                    timeout.Token
                );

                byte[] prefix = new byte[2];
                await stream.ReadExactlyAsync(prefix, timeout.Token);
                byte[] body = new byte[BinaryPrimitives.ReadUInt16BigEndian(prefix)];
                await stream.ReadExactlyAsync(body, timeout.Token);
                ReceivedQuery = DnsMessageReader.Read(body);

                byte[] replyBytes = DnsMessageWriter.Write(buildReply(ReceivedQuery));
                byte[] framed = new byte[2 + replyBytes.Length];
                BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)replyBytes.Length);
                replyBytes.CopyTo(framed.AsSpan(2));
                await stream.WriteAsync(framed, completeWrites: true, timeout.Token);

                // Disposing the connection at once could discard a reply the client has not read
                // yet; wait until the peer has acknowledged the end of the reply stream.
                await stream.WritesClosed.WaitAsync(timeout.Token);
            }
            catch (Exception exception)
                when (exception is QuicException or OperationCanceledException or IOException)
            {
                // The client gave up (a failed attempt); the assertions in the test report it.
            }
        }

        public async ValueTask DisposeAsync()
        {
            await listener.DisposeAsync();
            await serving;
            certificate.Dispose();
        }
    }

    private static UpstreamForwarderDefinition DefinitionFor(int port) =>
        new(
            UpstreamForwarderProtocolEnum.Quic,
            $"quic://127.0.0.1:{port}",
            IPAddress.Loopback,
            port,
            TlsHostName: "localhost"
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
    ///     This method builds a reply that repeats the query's question under the given ID.
    /// </summary>
    private static DnsMessage ReplyTo(DnsMessage query, ushort id)
    {
        DnsMessage reply = new()
        {
            Id = id,
            IsResponse = true,
            RecursionDesired = true,
            RecursionAvailable = true,
        };
        reply.Questions.AddRange(query.Questions);
        return reply;
    }

    private static bool IsQuicAvailable() => QuicListener.IsSupported && QuicConnection.IsSupported;

    [Fact]
    public async Task ForwardAsync_Always_SendsMessageIdZeroAndRestoresTheClientsIdOnTheReply()
    {
        if (!IsQuicAvailable())
            return;

        await using FakeDoqUpstream upstream = await FakeDoqUpstream.StartAsync(query =>
            ReplyTo(query, 0)
        );
        QuicForwarderClient client = new((_, _, _, _) => true);
        DnsMessage query = Query("www.example.com");
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));

        DnsMessage reply = await client.ForwardAsync(
            DefinitionFor(upstream.Port),
            query,
            timeout.Token
        );

        Assert.NotNull(upstream.ReceivedQuery);
        Assert.Equal(0, upstream.ReceivedQuery.Id);
        Assert.Equal(0x1234, query.Id);
        Assert.Equal(0x1234, reply.Id);
    }

    [Fact]
    public async Task ForwardAsync_ForAReplyWithANonZeroId_Throws()
    {
        if (!IsQuicAvailable())
            return;

        await using FakeDoqUpstream upstream = await FakeDoqUpstream.StartAsync(query =>
            ReplyTo(query, 5)
        );
        QuicForwarderClient client = new((_, _, _, _) => true);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.ForwardAsync(
                DefinitionFor(upstream.Port),
                Query("www.example.com"),
                timeout.Token
            )
        );
    }

    [Fact]
    public async Task ForwardAsync_ForAReplyToAnotherQuestion_Throws()
    {
        if (!IsQuicAvailable())
            return;

        await using FakeDoqUpstream upstream = await FakeDoqUpstream.StartAsync(query =>
        {
            DnsMessage reply = ReplyTo(query, 0);
            reply.Questions.Clear();
            reply.Questions.AddRange(Query("evil.example").Questions);
            return reply;
        });
        QuicForwarderClient client = new((_, _, _, _) => true);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.ForwardAsync(
                DefinitionFor(upstream.Port),
                Query("www.example.com"),
                timeout.Token
            )
        );
    }
}
