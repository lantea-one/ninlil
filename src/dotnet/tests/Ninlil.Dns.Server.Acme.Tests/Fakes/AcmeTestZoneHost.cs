using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Core.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Tests.Fakes;

/// <summary>
///     This class starts a small host with SQLite storage and an
///     <see cref="AuthoritativeZoneCache" /> that holds one Primary zone, <c>example.com</c>, whose
///     content a test chooses: notify addresses, NS records, glue and challenge records. The waiter
///     tests use it because the waiter reads the zone cache, which can only be filled from storage.
///     Disposing it stops the host and deletes the database.
/// </summary>
public sealed class AcmeTestZoneHost : IAsyncDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-acme-zone-host-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;

    /// <summary>The zone cache the host loaded after the seed ran.</summary>
    public AuthoritativeZoneCache Cache { get; private set; }

    /// <summary>
    ///     This method builds and starts the host. <paramref name="notifyAddresses" /> go on the zone
    ///     and <paramref name="records" /> (name, type, data) are stored next to an SOA before the
    ///     cache loads.
    /// </summary>
    public static async Task<AcmeTestZoneHost> StartAsync(
        string[] notifyAddresses,
        params (string Name, DnsResourceRecordTypeEnum Type, IDnsRecordData Data)[] records
    )
    {
        AcmeTestZoneHost instance = new();
        await instance.InitializeAsync(notifyAddresses, records);
        return instance;
    }

    private async Task InitializeAsync(
        string[] notifyAddresses,
        (string Name, DnsResourceRecordTypeEnum Type, IDnsRecordData Data)[] records
    )
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);
        builder.Services.AddSingleton<AuthoritativeZoneCache>();
        builder.Services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<AuthoritativeZoneCache>()
        );

        _host = builder.Build();

        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<NinlilDnsContext>()
                .Database.EnsureCreatedAsync();

            IZoneRepository zoneRepository =
                scope.ServiceProvider.GetRequiredService<IZoneRepository>();
            IRecordRepository recordRepository =
                scope.ServiceProvider.GetRequiredService<IRecordRepository>();
            Guid zoneId = Guid.NewGuid();

            await zoneRepository.AddAsync(
                new ZoneEntity
                {
                    Id = zoneId,
                    Name = "example.com",
                    Type = ZoneTypeEnum.Primary,
                    NotifyAddresses = [.. notifyAddresses],
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );

            (string Name, DnsResourceRecordTypeEnum Type, IDnsRecordData Data)[] all =
            [
                (
                    "example.com",
                    DnsResourceRecordTypeEnum.Soa,
                    new SoaRecordData(
                        DnsName.Parse("ns1.example.com"),
                        DnsName.Parse("admin.example.com"),
                        1,
                        3600,
                        900,
                        604800,
                        3600
                    )
                ),
                .. records,
            ];

            foreach ((string name, DnsResourceRecordTypeEnum type, IDnsRecordData data) in all)
                await recordRepository.AddAsync(
                    new RecordEntity
                    {
                        Id = Guid.NewGuid(),
                        ZoneId = zoneId,
                        Name = name,
                        Type = type,
                        Class = DnsClassEnum.Internet,
                        TimeToLiveSeconds = 300,
                        DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                            data.GetType(),
                            data
                        ),
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await _host.StartAsync();
        Cache = _host.Services.GetRequiredService<AuthoritativeZoneCache>();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }
}
