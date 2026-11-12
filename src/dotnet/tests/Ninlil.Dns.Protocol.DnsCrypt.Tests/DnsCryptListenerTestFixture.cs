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
    ///     This property contains the UDP and TCP port the shared listeners use. The UDP listener
    ///     binds port 0 and the TCP listener follows it (see
    ///     <see cref="DnsCryptListenerStartup.StartOnFreePortAsync" />), so it is set by
    ///     <see cref="InitializeAsync" />.
    /// </summary>
    public static int Port { get; private set; }

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
            Port = 0,
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

        (_udpListener, _tcpListener, int port) = await DnsCryptListenerStartup.StartOnFreePortAsync(
            dnscryptOptions,
            () =>
                (
                    new DnsCryptUdpListener(
                        _pipeline,
                        store,
                        optionsWrapped,
                        NullLogger<DnsCryptUdpListener>.Instance
                    ),
                    new DnsCryptTcpListener(
                        _pipeline,
                        store,
                        optionsWrapped,
                        NullLogger<DnsCryptTcpListener>.Instance
                    )
                )
        );

        if (port == 0)
            throw new InvalidOperationException("The DNSCrypt listeners did not bind.");

        Port = port;
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
}
