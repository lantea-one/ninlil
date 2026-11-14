using System.Buffers;
using System.Buffers.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Benchmarks.Pipeline;
using Ninlil.Dns.Protocol.DnsCrypt;
using Ninlil.Dns.Server.DnsCrypt;

// Define our namespace.
namespace Ninlil.Benchmarks.Transport;

/// <summary>
///     This class is the DNSCrypt resolver identity that the DNSCrypt transport benchmarks
///     (<see cref="DnsCryptUdpTransportBenchmarks" /> and
///     <see cref="DnsCryptTcpTransportBenchmarks" />) share: the options for one listener on
///     <c>127.0.0.1</c>, a <see cref="FileDnsCryptCertificateStore" /> over a state file in a
///     temporary directory, and the <see cref="DnsCryptCertificateRotationService" /> that fills
///     it with a signed certificate. It also builds the <c>sdns://</c> stamp a client uses to
///     find the resolver. The owner deletes the state directory through
///     <see cref="DeleteStateDirectory" /> when the benchmark ends or its setup fails.
/// </summary>
internal sealed class DnsCryptBenchmarkProvider
{
    /// <summary>
    ///     This constant contains the provider name of the benchmark resolver.
    /// </summary>
    public const string ProviderName = "bench.example";

    private readonly string _stateDirectory;
    private readonly DnsCryptCertificateRotationService _rotation;

    private DnsCryptBenchmarkProvider(
        string stateDirectory,
        ServiceProvider services,
        DnsCryptCertificateRotationService rotation
    )
    {
        _stateDirectory = stateDirectory;
        Services = services;
        _rotation = rotation;
    }

    /// <summary>
    ///     This property contains the container that resolves the options monitor, the store and
    ///     the loggers a listener needs.
    /// </summary>
    public ServiceProvider Services { get; }

    /// <summary>
    ///     This property contains the store that holds the signed certificate.
    /// </summary>
    public FileDnsCryptCertificateStore Store =>
        Services.GetRequiredService<FileDnsCryptCertificateStore>();

    /// <summary>
    ///     This property contains the options monitor a listener reads its settings through.
    /// </summary>
    public IOptionsMonitor<DnsCryptOptions> Options =>
        Services.GetRequiredService<IOptionsMonitor<DnsCryptOptions>>();

    /// <summary>
    ///     This method creates the state directory, writes the options for a listener on
    ///     <paramref name="port" />, starts the certificate rotation and waits until the store
    ///     holds a certificate. It removes the state directory itself when it throws.
    /// </summary>
    /// <param name="host">The pipeline host whose logger factory the services reuse.</param>
    /// <param name="port">The port the listener will bind.</param>
    public static async Task<DnsCryptBenchmarkProvider> StartAsync(
        PipelineBenchmarkHost host,
        int port
    )
    {
        string stateDirectory = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-bench-dnscrypt-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(stateDirectory);

        try
        {
            DnsCryptOptions dnsCrypt = new()
            {
                Enable = true,
                Port = port,
                ListenAddresses = "127.0.0.1",
                ProviderName = ProviderName,
                CertificateStateFilePath = Path.Combine(stateDirectory, "state.json"),
                EnabledEsVersions = [DnsCryptEsVersion.XChaCha20Poly1305],
                CertificateRotationInterval = TimeSpan.FromMinutes(10),
                CertificateValidityDuration = TimeSpan.FromMinutes(20),
                CertificateClockSkewTolerance = TimeSpan.Zero,
            };
            ServiceCollection services = new();
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(dnsCrypt));
            services.AddSingleton<IOptionsMonitor<DnsCryptOptions>>(
                new FixedOptionsMonitor(dnsCrypt)
            );
            services.AddSingleton<ILoggerFactory>(
                host.Services.GetRequiredService<ILoggerFactory>()
            );
            services.AddLogging();
            services.AddSingleton<FileDnsCryptCertificateStore>();
            ServiceProvider provider = services.BuildServiceProvider();
            FileDnsCryptCertificateStore store =
                provider.GetRequiredService<FileDnsCryptCertificateStore>();

            DnsCryptCertificateRotationService rotation = new(
                store,
                provider.GetRequiredService<IOptionsMonitor<DnsCryptOptions>>(),
                provider.GetRequiredService<ILogger<DnsCryptCertificateRotationService>>()
            );
            await rotation.StartAsync(CancellationToken.None);
            await WaitUntilAsync(() => store.Entries.Count > 0);

            return new DnsCryptBenchmarkProvider(stateDirectory, provider, rotation);
        }
        catch
        {
            if (Directory.Exists(stateDirectory))
                Directory.Delete(stateDirectory, recursive: true);

            throw;
        }
    }

    /// <summary>
    ///     This method stops the certificate rotation.
    /// </summary>
    public Task StopRotationAsync() => _rotation.StopAsync(CancellationToken.None);

    /// <summary>
    ///     This method removes the state directory with the certificate files the run wrote.
    /// </summary>
    public void DeleteStateDirectory()
    {
        if (_stateDirectory != null && Directory.Exists(_stateDirectory))
            Directory.Delete(_stateDirectory, recursive: true);
    }

    /// <summary>
    ///     This method builds the <c>sdns://</c> stamp of this resolver at
    ///     <paramref name="address" /> (a host:port), as DNSCrypt's stamp format lays it out:
    ///     the protocol byte, eight property bytes, the address, the provider public key and the
    ///     provider name, each of the last three with a one-byte length prefix.
    /// </summary>
    public string BuildStamp(string address)
    {
        ArrayBufferWriter<byte> buffer = new();
        buffer.GetSpan(1)[0] = 0x01;
        buffer.Advance(1);
        buffer.GetSpan(8).Clear();
        buffer.Advance(8);

        WriteLengthPrefixed(buffer, System.Text.Encoding.ASCII.GetBytes(address));
        WriteLengthPrefixed(buffer, Store.ProviderKeyPair.PublicKeyWireFormat);
        WriteLengthPrefixed(
            buffer,
            System.Text.Encoding.UTF8.GetBytes($"2.dnscrypt-cert.{ProviderName}")
        );

        return "sdns://" + Base64Url.EncodeToString(buffer.WrittenSpan);
    }

    private static void WriteLengthPrefixed(IBufferWriter<byte> buffer, byte[] value)
    {
        buffer.GetSpan(1)[0] = (byte)value.Length;
        buffer.Advance(1);
        value.CopyTo(buffer.GetSpan(value.Length));
        buffer.Advance(value.Length);
    }

    /// <summary>
    ///     This method polls until <paramref name="condition" /> holds and throws when it does not
    ///     within five seconds. A BackgroundService binds and generates on a continuation after
    ///     <c>StartAsync</c> returns, so a caller waits for its effect.
    /// </summary>
    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 500 && !condition(); attempt++)
            await Task.Delay(10);

        if (!condition())
            throw new InvalidOperationException("The DNSCrypt listener did not start.");
    }

    /// <summary>
    ///     This class is an <see cref="IOptionsMonitor{TOptions}" /> that always returns one
    ///     value; the listener and the rotation service read their settings through it.
    /// </summary>
    private sealed class FixedOptionsMonitor(DnsCryptOptions value)
        : IOptionsMonitor<DnsCryptOptions>
    {
        /// <inheritdoc />
        public DnsCryptOptions CurrentValue => value;

        /// <inheritdoc />
        public DnsCryptOptions Get(string name) => value;

        /// <inheritdoc />
        public IDisposable OnChange(Action<DnsCryptOptions, string> listener) => null;
    }
}
