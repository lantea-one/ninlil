using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Extension;
using Ninlil.Dns.Server.Core.Extension;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Dnssec.Algorithms;
using Ninlil.Dns.Server.Dnssec.Extension;
using Ninlil.Dns.Server.Tsig.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Dnssec;

/// <summary>
///     This class tests that <see cref="ZoneSigningService" /> never signs with a key whose
///     algorithm <see cref="DnssecAlgorithmRegistry.IsSigningProhibited" /> names and never
///     publishes it in the DNSKEY RRset. It runs the
///     service against a real SQLite database and a zone holding an ECDSA P-256 key and a legacy
///     RSASHA1 key (written to storage directly, because key generation now refuses RSASHA1), and
///     captures log output to count the warnings.
/// </summary>
public sealed class ZoneSigningServiceProhibitedAlgorithmTests : IAsyncLifetime
{
    private const string ZoneName = "prohibited-algorithm.example.com";

    /// <summary>
    ///     This class is a logger provider that keeps the Warning messages written for the zone
    ///     signing service.
    /// </summary>
    private sealed class WarningCollector : ILoggerProvider, ILogger
    {
        public readonly List<string> Warnings = [];

        public ILogger CreateLogger(string categoryName) =>
            categoryName == typeof(ZoneSigningService).FullName ? this : NullLogger();

        private static ILogger NullLogger() =>
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                lock (Warnings)
                    Warnings.Add(formatter(state, exception));
        }

        public void Dispose() { }
    }

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-prohibited-algorithm-tests-{Guid.NewGuid()}.sqlite3"
    );

    private readonly WarningCollector _warnings = new();
    private IHost _host;
    private Guid _zoneId;
    private ushort _rsaSha1KeyTag;
    private ushort _ecdsaKeyTag;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Logging.AddProvider(_warnings);
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);
        builder.Services.AddSingleton<IDataProtectionProvider>(
            new EphemeralDataProtectionProvider()
        );
        builder.Services.AddNinlilDnsServerTsig(builder.Configuration);
        builder.Services.AddNinlilDnsServerDnssec();
        builder.Services.AddNinlilDnsServerAuthoritative(builder.Configuration);

        _host = builder.Build();

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IDnssecKeyRepository keyRepository =
            scope.ServiceProvider.GetRequiredService<IDnssecKeyRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        IDnssecKeyManagementService keyManagement =
            scope.ServiceProvider.GetRequiredService<IDnssecKeyManagementService>();

        _zoneId = Guid.NewGuid();
        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _zoneId,
                Name = ZoneName,
                Type = ZoneTypeEnum.Primary,
                IsDnssecSigned = true,
                DnssecAlgorithm = DnssecAlgorithmEnum.EcdsaP256Sha256,
                DenialOfExistenceMode = DnssecDenialOfExistenceModeEnum.Nsec,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await recordRepository.AddAsync(
            new RecordEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = _zoneId,
                Name = ZoneName,
                Type = DnsResourceRecordTypeEnum.Soa,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 3600,
                DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                    typeof(SoaRecordData),
                    new SoaRecordData(
                        DnsName.Parse($"ns1.{ZoneName}"),
                        DnsName.Parse($"admin.{ZoneName}"),
                        1,
                        3600,
                        900,
                        604800,
                        3600
                    )
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        DnssecKeyEntity ecdsaKey = await keyManagement.GenerateKeyAsync(
            _zoneId,
            DnssecKeyRoleEnum.Zsk,
            DnssecAlgorithmEnum.EcdsaP256Sha256
        );
        _ecdsaKeyTag = ecdsaKey.KeyTag;

        // A key stored before the prohibition existed. It is written straight to the repository
        // with a real RSA key pair, as the earlier key generation would have left it.
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            DnssecAlgorithmEnum.RsaSha1,
            out IDnssecSigningAlgorithm rsaSha1
        );
        DnssecKeyPair pair = rsaSha1.GenerateKeyPair(2048);
        byte[] rdata = DnsRecordDataCodecRegistry.Encode(
            DnsResourceRecordTypeEnum.DnsKey,
            new DnsKeyRecordData(true, false, DnssecAlgorithmEnum.RsaSha1, pair.PublicKeyWireFormat)
        );
        _rsaSha1KeyTag = DnssecKeyTag.Compute(DnssecAlgorithmEnum.RsaSha1, rdata);
        await keyRepository.AddAsync(
            new DnssecKeyEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = _zoneId,
                Role = DnssecKeyRoleEnum.Zsk,
                Algorithm = DnssecAlgorithmEnum.RsaSha1,
                PublicKeyWireFormat = pair.PublicKeyWireFormat,
                PrivateKeyPkcs8Encrypted = scope
                    .ServiceProvider.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("Ninlil.Dns.Server.Dnssec.PrivateKey")
                    .Protect(Convert.ToBase64String(pair.PrivateKeyPkcs8)),
                KeyTag = _rsaSha1KeyTag,
                LifecycleState = DnssecKeyLifecycleStateEnum.Active,
                PublishedAtUtc = DateTimeOffset.UtcNow,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
    }

    [Fact]
    public async Task SignZoneAsync_WithAnRsaSha1KeyAndAnEcdsaKey_PublishesAndSignsWithEcdsaOnlyAndWarnsOnce()
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IZoneSigningService signingService =
            scope.ServiceProvider.GetRequiredService<IZoneSigningService>();

        SignedZoneData signed = await signingService.SignZoneAsync(_zoneId);

        // The RSASHA1 key is not published, so the DNSKEY RRset holds the ECDSA key only.
        List<DnsKeyRecordData> published =
        [
            .. signed.DnsKeyRecords.Select(record =>
                (DnsKeyRecordData)
                    DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.DnsKey, record.Data)
            ),
        ];
        Assert.DoesNotContain(published, key => key.Algorithm == DnssecAlgorithmEnum.RsaSha1);
        Assert.Contains(published, key => key.Algorithm == DnssecAlgorithmEnum.EcdsaP256Sha256);

        List<RrSigRecordData> signatures =
        [
            .. signed
                .RrsigsByCoveredRrset.Values.SelectMany(records => records)
                .Select(record =>
                    (RrSigRecordData)
                        DnsRecordDataCodecRegistry.Decode(
                            DnsResourceRecordTypeEnum.RrSig,
                            record.Data
                        )
                ),
        ];
        Assert.NotEmpty(signatures);
        Assert.All(
            signatures,
            signature => Assert.Equal(DnssecAlgorithmEnum.EcdsaP256Sha256, signature.Algorithm)
        );
        Assert.DoesNotContain(signatures, signature => signature.KeyTag == _rsaSha1KeyTag);
        Assert.Contains(signatures, signature => signature.KeyTag == _ecdsaKeyTag);

        // One Warning for the key, not one per RRset.
        Assert.Single(_warnings.Warnings);
        Assert.Contains("prohibited", _warnings.Warnings[0], StringComparison.Ordinal);
        Assert.Contains("not published", _warnings.Warnings[0], StringComparison.Ordinal);
        Assert.Contains(ZoneName, _warnings.Warnings[0], StringComparison.Ordinal);
    }
}
