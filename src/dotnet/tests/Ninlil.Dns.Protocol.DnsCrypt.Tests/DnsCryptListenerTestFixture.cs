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
///     This class stands up the real DNSCrypt UDP and TCP listeners against a real
///     <see cref="DnsQueryProcessingPipeline" /> and <see cref="EchoResolver" />, backed by two
///     real, Ed25519-signed certificates (one per supported cipher) -- everything every test in
///     this suite sends a real packet against, over real loopback sockets.
/// </summary>
public sealed class DnsCryptListenerTestFixture : IAsyncLifetime
{
    /// <summary>
    ///     This constant contains the fixed UDP/TCP port every test in this suite connects to.
    /// </summary>
    public const int Port = 45463;

    /// <summary>
    ///     This constant contains the provider name this suite's resolver advertises its
    ///     certificates under.
    /// </summary>
    public const string ProviderName = "example.test";

    private DnsQueryProcessingPipeline _pipeline;
    private DnsCryptUdpListener _udpListener;
    private DnsCryptTcpListener _tcpListener;

    /// <summary>
    ///     This property contains this resolver's own long-term Ed25519 provider key pair, used
    ///     by every test to independently re-verify a discovered certificate's signature.
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

        // Both listeners bind their socket synchronously as the very first thing their own
        // background loop does, but that loop is still scheduled as a continuation rather than
        // run fully before StartAsync returns (the same BackgroundService timing behavior
        // observed while writing Ninlil.Dns.Server.DnsCrypt's own rotation-service tests) --
        // give both a moment to actually bind before any test tries to connect.
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

    private static DnsCryptCertificateEntry BuildSignedCertificate(
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
