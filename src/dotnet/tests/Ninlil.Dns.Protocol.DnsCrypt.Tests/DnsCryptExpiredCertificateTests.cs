using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Common.Extension.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.DnsCrypt.Tests.Fakes;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.DnsCrypt;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt.Tests;

/// <summary>
///     This class tests that the DNSCrypt UDP listener decrypts a query only with a certificate
///     that is valid now. DNSCrypt v2 section 13 says a resolver must throw away the previous
///     secret key once it has rotated, so a certificate that is past its <c>ts-end</c> but still
///     in the store (the rotation service drops it only at its next wake-up) must not answer.
///     Each test starts a UDP listener on an OS-assigned loopback port over a store that holds one
///     certificate, and sends it a real encrypted query.
/// </summary>
public sealed class DnsCryptExpiredCertificateTests
{
    [Fact]
    public async Task Udp_AQueryForACertificateThatExpiredTwoSecondsAgo_GetsNoReply()
    {
        byte[] reply = await QueryAsync(
            validFromOffsetSeconds: -3600,
            validUntilOffsetSeconds: -2,
            replyWait: SilenceWindow
        );

        Assert.Null(reply);
    }

    [Fact]
    public async Task Udp_AQueryForACertificateThatIsStillValid_GetsTheEchoAnswer()
    {
        byte[] reply = await QueryAsync(
            validFromOffsetSeconds: -3600,
            validUntilOffsetSeconds: 3600,
            replyWait: ReplyTimeout
        );

        Assert.NotNull(reply);
    }

    /// <summary>
    ///     This constant is how long the test that expects a reply waits for it. It is a failure
    ///     bound, not a delay: the wait ends when the reply arrives, so a loaded machine that
    ///     answers late still passes.
    /// </summary>
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This constant is how long the tests that expect silence wait to confirm it. A silent
    ///     listener always costs the whole window, so it stays short; a stalled machine can only
    ///     make such a test pass, never fail.
    /// </summary>
    private static readonly TimeSpan SilenceWindow = TimeSpan.FromMilliseconds(500);

    /// <summary>
    ///     This method starts a UDP listener over a store holding one XChaCha20-Poly1305
    ///     certificate valid from <paramref name="validFromOffsetSeconds" /> to
    ///     <paramref name="validUntilOffsetSeconds" /> seconds around now, sends it one encrypted
    ///     query, and returns the reply packet, or <see langword="null" /> when none arrives
    ///     within <paramref name="replyWait" />.
    /// </summary>
    private static async Task<byte[]> QueryAsync(
        int validFromOffsetSeconds,
        int validUntilOffsetSeconds,
        TimeSpan replyWait
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        (byte[] publicKey, byte[] secretKey) = DnsCryptCryptoBox.GenerateKeyPair(
            DnsCryptEsVersion.XChaCha20Poly1305
        );
        DnsCryptCertificateEntry entry = new(
            new DnsCryptCertificate(
                DnsCryptEsVersion.XChaCha20Poly1305,
                publicKey,
                RandomNumberGenerator.GetBytes(8),
                1,
                now.AddSeconds(validFromOffsetSeconds),
                now.AddSeconds(validUntilOffsetSeconds),
                new byte[64]
            ),
            secretKey
        );
        InMemoryDnsCryptCertificateStore store = new(null);
        store.Save([entry]);

        StaticOptionsMonitor<DnsCryptOptions> options = new(
            new DnsCryptOptions
            {
                Enable = true,
                Port = 0,
                ProviderName = DnsCryptListenerTestFixture.ProviderName,
                ListenAddresses = "127.0.0.1",
            }
        );
        DnsQueryProcessingPipeline pipeline = new(
            new DnsQueryDispatcher(
                [new EchoResolver()],
                new DnsServerEventPublisher(
                    Options.Create(new DnsServerEventPublisherOptions()),
                    NullLogger<DnsServerEventPublisher>.Instance
                )
            ),
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance
        );
        DnsCryptUdpListener listener = new(
            pipeline,
            store,
            options,
            NullLogger<DnsCryptUdpListener>.Instance
        );

        try
        {
            await pipeline.StartAsync(CancellationToken.None);
            await listener.StartAsync(CancellationToken.None);

            // The listener binds on its background loop. Port 0 lets the OS pick the port, and
            // IsListening turns true only once the socket is bound, so the client never sends
            // to a port nobody listens on yet.
            for (int attempt = 0; attempt < 500 && !listener.IsListening; attempt++)
                await Task.Delay(10);

            Assert.True(listener.IsListening);

            (byte[] packet, _) = DnsCryptWireTestHelper.BuildEncryptedQueryPacket(
                entry,
                DnsCryptWireTestHelper.BuildEchoQuery(7)
            );
            using UdpClient client = new();

            // Windows reports an ICMP port-unreachable for an earlier datagram as WSAECONNRESET on
            // the next receive of a connected UDP socket; switching it off keeps a stray one from
            // failing the receive this test waits on.
            client.Client.IgnoreIcmpPortUnreachable();
            client.Connect(IPAddress.Loopback, listener.BoundPort);
            await client.SendAsync(packet);

            using CancellationTokenSource timeout = new(replyWait);

            try
            {
                return (await client.ReceiveAsync(timeout.Token)).Buffer;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
            await pipeline.StopAsync(CancellationToken.None);
        }
    }
}
