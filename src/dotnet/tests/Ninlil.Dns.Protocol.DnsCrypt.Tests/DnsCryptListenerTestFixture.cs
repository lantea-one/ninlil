using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Protocol.DnsCrypt.Tests.Fakes;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt.Tests;

/// <summary>
///     This class starts the DNSCrypt UDP and TCP listeners on loopback sockets, with a
///     <see cref="DnsQueryProcessingPipeline" />, the fake <see cref="EchoResolver" />, and two
///     Ed25519-signed certificates (one per supported cipher) in an in-memory store. The
///     <see cref="DnsCryptListenerTests" /> share it.
/// </summary>
public sealed class DnsCryptListenerTestFixture : IAsyncLifetime
{
    /// <summary>
    ///     This field contains the UDP and TCP port the shared listeners use, chosen free at
    ///     startup.
    /// </summary>
    public static readonly int Port = GetFreePort();

    /// <summary>
    ///     This constant contains the provider name the certificates are published under.
    /// </summary>
    public const string ProviderName = "example.test";

    private DnsQueryProcessingPipeline _pipeline;
    private DnsCryptUdpListener _udpListener;
    private DnsCryptTcpListener _tcpListener;

    /// <summary>
    ///     This property contains the long-term Ed25519 provider key pair, which tests use to
    ///     verify a discovered certificate's signature.
    /// </summary>
    public DnssecKeyPair ProviderKeyPair { get; private set; }

    /// <summary>
    ///     This property contains the certificate entry issued for each supported cipher.
    /// </summary>
    public Dictionary<
        DnsCryptEsVersion,
        DnsCryptCertificateEntry
    > CertificatesByEsVersion { get; } = [];

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        if (
            !DnssecAlgorithmRegistry.TryGetAlgorithm(
                DnssecAlgorithmEnum.Ed25519,
                out IDnssecSigningAlgorithm algorithm
            )
        )
            throw new InvalidOperationException("Ed25519 is not registered.");

        ProviderKeyPair = algorithm.GenerateKeyPair();

        foreach (
            DnsCryptEsVersion esVersion in new[]
            {
                DnsCryptEsVersion.XChaCha20Poly1305,
                DnsCryptEsVersion.XSalsa20Poly1305,
            }
        )
            CertificatesByEsVersion[esVersion] = BuildSignedCertificate(
                algorithm,
                ProviderKeyPair,
                esVersion
            );

        InMemoryDnsCryptCertificateStore store = new(ProviderKeyPair);
        store.Save([.. CertificatesByEsVersion.Values]);

        DnsCryptOptions dnscryptOptions = new()
        {
            Enable = true,
            Port = Port,
            ProviderName = ProviderName,
        };
        IOptionsMonitor<DnsCryptOptions> optionsWrapped = new StaticOptionsMonitor<DnsCryptOptions>(
            dnscryptOptions
        );

        DnsServerEventPublisher eventPublisher = new(
            Options.Create(new DnsServerEventPublisherOptions()),
            NullLogger<DnsServerEventPublisher>.Instance
        );
        DnsQueryDispatcher dispatcher = new([new EchoResolver()], eventPublisher);

        _pipeline = new DnsQueryProcessingPipeline(
            dispatcher,
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance
        );
        await _pipeline.StartAsync(CancellationToken.None);

        _udpListener = new DnsCryptUdpListener(
            _pipeline,
            store,
            optionsWrapped,
            NullLogger<DnsCryptUdpListener>.Instance
        );
        _tcpListener = new DnsCryptTcpListener(
            _pipeline,
            store,
            optionsWrapped,
            NullLogger<DnsCryptTcpListener>.Instance
        );

        await _udpListener.StartAsync(CancellationToken.None);
        await _tcpListener.StartAsync(CancellationToken.None);

        // Each listener binds at the start of its background loop, which BackgroundService runs
        // as a continuation after StartAsync returns, so give both a moment to bind.
        await Task.Delay(200);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_udpListener is not null)
            await _udpListener.StopAsync(CancellationToken.None);

        if (_tcpListener is not null)
            await _tcpListener.StopAsync(CancellationToken.None);

        if (_pipeline is not null)
            await _pipeline.StopAsync(CancellationToken.None);
    }

    internal static DnsCryptCertificateEntry BuildSignedCertificate(
        IDnssecSigningAlgorithm algorithm,
        DnssecKeyPair providerKeyPair,
        DnsCryptEsVersion esVersion
    )
    {
        (byte[] resolverPublicKey, byte[] resolverSecretKey) = DnsCryptCryptoBox.GenerateKeyPair(
            esVersion
        );
        DateTimeOffset now = DateTimeOffset.UtcNow;

        DnsCryptCertificate unsigned = new(
            esVersion,
            resolverPublicKey,
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(8),
            1,
            now.AddMinutes(-5),
            now.AddHours(48),
            []
        );

        byte[] signature = algorithm.Sign(
            DnsCryptCertificateCodec.BuildSignedData(unsigned),
            providerKeyPair.PrivateKeyPkcs8
        );

        return new DnsCryptCertificateEntry(
            unsigned with
            {
                Signature = signature,
            },
            resolverSecretKey
        );
    }

    /// <summary>
    ///     This method asks the operating system for a currently unused port, by binding to port 0
    ///     on loopback and reading back what was assigned. Tests run on shared machines with their
    ///     own DNS, DHCP and web services, and may run alongside another copy of this suite, so no
    ///     test listener uses a fixed or well-known port.
    /// </summary>
    /// <returns>A free TCP port number.</returns>
    private static int GetFreePort()
    {
        using System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}
