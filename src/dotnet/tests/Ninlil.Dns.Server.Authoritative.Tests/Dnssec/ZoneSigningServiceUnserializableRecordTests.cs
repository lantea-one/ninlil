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
using Ninlil.Dns.Server.Dnssec.Extension;
using Ninlil.Dns.Server.Tsig.Extension;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Dnssec;

/// <summary>
///     This class tests that <see cref="ZoneSigningService" /> skips a stored record whose data
///     cannot be written as RDATA (an A record with no address, a TXT record with no text) and
///     logs a Warning for it, instead of throwing and leaving the zone on its previous signatures.
///     It runs the service against a real SQLite database and writes the bad rows to storage
///     directly, because the REST API refuses them.
/// </summary>
public sealed class ZoneSigningServiceUnserializableRecordTests : IAsyncLifetime
{
    private const string ZoneName = "unserializable.example.com";

    /// <summary>
    ///     This class is a logger provider that keeps the Warning messages written for the zone
    ///     signing service.
    /// </summary>
    private sealed class WarningCollector : ILoggerProvider, ILogger
    {
        public readonly List<string> Warnings = [];

        public ILogger CreateLogger(string categoryName) =>
            categoryName == typeof(ZoneSigningService).FullName
                ? this
                : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

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
        $"ninlil-dns-unserializable-record-tests-{Guid.NewGuid()}.sqlite3"
    );

    private readonly WarningCollector _warnings = new();
    private IHost _host;
    private Guid _zoneId;

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

        await AddRecordAsync(
            recordRepository,
            ZoneName,
            DnsResourceRecordTypeEnum.Soa,
            Ninlil.Common.Serializer.JsonSerializer.Serialize(
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
            )
        );
        await AddRecordAsync(
            recordRepository,
            $"good.{ZoneName}",
            DnsResourceRecordTypeEnum.A,
            Ninlil.Common.Serializer.JsonSerializer.Serialize(
                typeof(ARecordData),
                new ARecordData(System.Net.IPAddress.Parse("203.0.113.5"))
            )
        );
        await AddRecordAsync(
            recordRepository,
            $"bad-a.{ZoneName}",
            DnsResourceRecordTypeEnum.A,
            """{"Address":null}"""
        );
        await AddRecordAsync(
            recordRepository,
            $"bad-txt.{ZoneName}",
            DnsResourceRecordTypeEnum.Txt,
            """{"Values":null}"""
        );
        await unitOfWork.SaveChangesAsync();

        await keyManagement.GenerateKeyAsync(
            _zoneId,
            DnssecKeyRoleEnum.Zsk,
            DnssecAlgorithmEnum.EcdsaP256Sha256
        );
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
    }

    private async Task AddRecordAsync(
        IRecordRepository recordRepository,
        string name,
        DnsResourceRecordTypeEnum type,
        string dataJson
    ) =>
        await recordRepository.AddAsync(
            new RecordEntity
            {
                Id = Guid.NewGuid(),
                ZoneId = _zoneId,
                Name = name,
                Type = type,
                Class = DnsClassEnum.Internet,
                TimeToLiveSeconds = 3600,
                DataJson = dataJson,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );

    [Fact]
    public async Task SignZoneAsync_WithRecordsWhoseDataCannotBeWritten_SkipsThemWarnsAndSignsTheRest()
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IZoneSigningService signingService =
            scope.ServiceProvider.GetRequiredService<IZoneSigningService>();

        SignedZoneData signed = await signingService.SignZoneAsync(_zoneId);

        // The good A record is signed; the two bad rows have no RRset and no signature.
        Assert.Contains(
            signed.RrsigsByCoveredRrset.Keys,
            key =>
                key.Name == DnsName.Parse($"good.{ZoneName}")
                && key.Type == DnsResourceRecordTypeEnum.A
        );
        Assert.DoesNotContain(
            signed.RrsigsByCoveredRrset.Keys,
            key => key.Name == DnsName.Parse($"bad-a.{ZoneName}")
        );
        Assert.DoesNotContain(
            signed.RrsigsByCoveredRrset.Keys,
            key => key.Name == DnsName.Parse($"bad-txt.{ZoneName}")
        );

        // One Warning per skipped record, naming the zone, the owner name and the type.
        Assert.Equal(2, _warnings.Warnings.Count);
        Assert.Contains(
            _warnings.Warnings,
            warning => warning.Contains($"bad-a.{ZoneName}", StringComparison.Ordinal)
        );
        Assert.Contains(
            _warnings.Warnings,
            warning => warning.Contains($"bad-txt.{ZoneName}", StringComparison.Ordinal)
        );
    }
}
