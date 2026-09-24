using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Transport;
using Ninlil.Dns.Server.DnsCrypt;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt;

/// <summary>
///     This class implements DNSCrypt's TCP fallback transport: the same two-octet big-endian
///     length prefix RFC 1035 §4.2.2/RFC 7766 already define for plain DNS-over-TCP, wrapping
///     either a plaintext certificate discovery query or an encrypted client query, exactly as
///     <see cref="DnsCryptUdpListener" /> already distinguishes over UDP -- via the same shared
///     <see cref="DnsCryptQueryHandler" />.
/// </summary>
/// <param name="pipeline">The pipeline every decrypted query is enqueued onto.</param>
/// <param name="store">The store this resolver's own tracked certificates are read from.</param>
/// <param name="options">
///     This transport's own listen addresses, port, provider name, and connection limits -- read via
///     <see cref="IOptionsMonitor{TOptions}" /> so a REST-driven <c>enable</c> toggle is genuinely
///     observed the next time <see cref="ExecuteAsync" /> runs, the same reasoning
///     <c>Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsListener</c>'s own <c>options</c>
///     parameter documents.
/// </param>
/// <param name="logger">The logger this listener records connection-level failures to.</param>
public sealed class DnsCryptTcpListener(
    DnsQueryProcessingPipeline pipeline,
    IDnsCryptCertificateStore store,
    IOptionsMonitor<DnsCryptOptions> options,
    ILogger<DnsCryptTcpListener> logger
) : BackgroundService
{
    /// <summary>
    ///     This property contains the listening sockets, one per listen address (or a single
    ///     dual-stack one when none are configured) -- kept here so <see cref="StopAsync" /> can
    ///     close them, unblocking each accept loop's pending accept call during shutdown.
    /// </summary>
    private ListenSocketSet _listenSockets = ListenSocketSet.Empty;

    /// <summary>
    ///     This property bounds how many connections may be accepted concurrently.
    /// </summary>
    private SemaphoreSlim _connectionLimiter;

    /// <summary>
    ///     This property reports whether this listener's own socket is currently bound and
    ///     accepting connections -- read by the <c>/status</c> health-check endpoint, which needs
    ///     a real "am I bound and running" signal from every transport rather than just the fact
    ///     that it was registered/configured.
    /// </summary>
    public bool IsListening { get; private set; }

    /// <summary>
    ///     This property lists every configured listen address this listener is not serving, each
    ///     as "address (reason)" -- read by the <c>/status</c> health check.
    /// </summary>
    public IReadOnlyList<string> FailedListenAddresses => _listenSockets.FailedAddresses;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.Enable)
            return;

        if (string.IsNullOrWhiteSpace(options.CurrentValue.ProviderName))
        {
            // DnsCryptUdpListener already logs this same misconfiguration; both transports
            // share one Enable toggle and one ProviderName, so both independently refuse to
            // start rather than one silently answering queries the other cannot.
            logger.LogError(
                "DNSCrypt is enabled, but no ProviderName is configured -- clients would have no name to discover this resolver's certificates under. This transport will not start; every other part of this server is unaffected."
            );
            return;
        }

        DnsName providerCertificateName =
            DnsCryptCertificateDiscoveryResponder.BuildProviderCertificateName(
                options.CurrentValue.ProviderName
            );

        _connectionLimiter = new SemaphoreSlim(options.CurrentValue.MaxConcurrentTcpConnections);

        _listenSockets = ListenSocketSet.Bind(
            "DNSCrypt (TCP)",
            options.CurrentValue.ListenAddresses,
            options.CurrentValue.Port,
            ProtocolType.Tcp,
            logger
        );

        if (_listenSockets.Sockets.Count == 0)
            return;

        IsListening = true;

        await Task.WhenAll(
            _listenSockets.Sockets.Select(socket =>
                AcceptLoopAsync(socket, providerCertificateName, stoppingToken)
            )
        );
    }

    /// <summary>
    ///     This method accepts connections on one listening socket until shutdown; each listen
    ///     address gets its own loop, all sharing one connection limit.
    /// </summary>
    private async Task AcceptLoopAsync(
        Socket listenSocket,
        DnsName providerCertificateName,
        CancellationToken stoppingToken
    )
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Socket connection;

            try
            {
                connection = await listenSocket.AcceptAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                continue;
            }

            if (!_connectionLimiter.Wait(0))
            {
                connection.Close();
                continue;
            }

            _ = HandleConnectionAsync(connection, providerCertificateName, stoppingToken);
        }
    }

    /// <summary>
    ///     This method owns a single accepted connection for its entire lifetime: reading every
    ///     length-prefixed packet it sends, routing each in turn through
    ///     <see cref="DnsCryptQueryHandler" />, until the idle timeout elapses or the client
    ///     disconnects.
    /// </summary>
    private async Task HandleConnectionAsync(
        Socket connection,
        DnsName providerCertificateName,
        CancellationToken stoppingToken
    )
    {
        IPEndPoint remoteEndpoint = (IPEndPoint)connection.RemoteEndPoint;
        SemaphoreSlim writeLock = new(1, 1);

        try
        {
            using CancellationTokenSource idleCts = CancellationTokenSource.CreateLinkedTokenSource(
                stoppingToken
            );

            while (true)
            {
                idleCts.CancelAfter(options.CurrentValue.TcpIdleTimeout);

                byte[] lengthPrefix = new byte[2];

                if (!await ReadExactAsync(connection, lengthPrefix, idleCts.Token))
                    break;

                ushort packetLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);
                byte[] packetBuffer = new byte[packetLength];

                if (!await ReadExactAsync(connection, packetBuffer, idleCts.Token))
                    break;

                await DnsCryptQueryHandler.HandlePacketAsync(
                    packetBuffer,
                    remoteEndpoint.Address,
                    DnsTransportProtocolEnum.DnsCrypt,
                    providerCertificateName,
                    pipeline,
                    store,
                    logger,
                    (responseBytes, sendToken) =>
                        WriteFramedAsync(connection, writeLock, responseBytes, sendToken),
                    idleCts.Token
                );
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(
                exception,
                "DNSCrypt TCP connection from {Endpoint} ended abnormally.",
                remoteEndpoint
            );
        }
        finally
        {
            connection.Close();
            _connectionLimiter.Release();
        }
    }

    /// <summary>
    ///     This method writes a single length-prefixed packet to a connection, serialized
    ///     against every other write on that same connection, the same reasoning
    ///     <c>Ninlil.Dns.Protocol.Plain.TcpDnsListener</c> already documents on its
    ///     own equivalent method.
    /// </summary>
    private static async ValueTask WriteFramedAsync(
        Socket connection,
        SemaphoreSlim writeLock,
        ReadOnlyMemory<byte> packetBytes,
        CancellationToken stoppingToken
    )
    {
        await writeLock.WaitAsync(stoppingToken);

        try
        {
            byte[] lengthPrefix = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)packetBytes.Length);

            await connection.SendAsync(lengthPrefix, stoppingToken);
            await connection.SendAsync(packetBytes, stoppingToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <summary>
    ///     This method fills a buffer completely from a socket, or reports a clean disconnect.
    /// </summary>
    private static async ValueTask<bool> ReadExactAsync(
        Socket connection,
        byte[] buffer,
        CancellationToken stoppingToken
    )
    {
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read = await connection.ReceiveAsync(buffer.AsMemory(totalRead), stoppingToken);

            if (read == 0)
            {
                if (totalRead == 0)
                    return false;

                throw new IOException("The connection closed partway through a DNSCrypt packet.");
            }

            totalRead += read;
        }

        return true;
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        IsListening = false;
        _listenSockets.Close();
        await base.StopAsync(cancellationToken);
    }
}
