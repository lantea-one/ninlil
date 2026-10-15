using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
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
using Ninlil.Dns.Server.Authoritative.CloudSync;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.CloudSync;

/// <summary>
///     This class runs <see cref="CloudDnsZoneSyncService" /> end to end against
///     <see cref="FakeRoute53Server" /> with <see cref="Route53DnsProvider" />, in both directions,
///     with the EF Core repositories on a temporary SQLite database. A CloudPrimary zone pushes
///     record sets with several values and a wildcard owner; a CloudSecondary zone pulls a set with
///     three values. A second pass must change nothing on either side (no re-create loop).
/// </summary>
public sealed class Route53ZoneSyncRoundTripTests : IAsyncLifetime
{
    private const string PushZoneId = "ZPUSH";
    private const string PullZoneId = "ZPULL";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-route53-sync-tests-{Guid.NewGuid()}.sqlite3"
    );

    private readonly FakeRoute53Server _fake = new();
    private IHost _host;
    private Guid _pullZoneId;

    public async Task InitializeAsync()
    {
        await _fake.StartAsync();

        // The provider's own apex NS and SOA sets; the sync must leave them alone.
        _fake.AddSet(
            PushZoneId,
            new FakeRoute53Server.FakeRecordSet
            {
                Name = "r53push.example.com.",
                Type = "NS",
                Ttl = 172800,
                Values = ["ns-1.awsdns-01.org.", "ns-2.awsdns-02.com."],
            }
        );
        _fake.AddSet(
            PushZoneId,
            new FakeRoute53Server.FakeRecordSet
            {
                Name = "r53push.example.com.",
                Type = "SOA",
                Ttl = 900,
                Values =
                [
                    "ns-1.awsdns-01.org. awsdns-hostmaster.amazon.com. 1 7200 900 1209600 86400",
                ],
            }
        );
        // An alias set Ninlil cannot represent; it must survive both passes untouched.
        _fake.AddSet(
            PushZoneId,
            new FakeRoute53Server.FakeRecordSet
            {
                Name = "lb.r53push.example.com.",
                Type = "A",
                AliasDnsName = "lb.elb.amazonaws.com.",
            }
        );
        _fake.AddSet(
            PullZoneId,
            new FakeRoute53Server.FakeRecordSet
            {
                Name = "txt.r53pull.example.com.",
                Type = "TXT",
                Values = ["\"first\"", "\"second\"", "\"third\""],
            }
        );
        _fake.AddSet(
            PullZoneId,
            new FakeRoute53Server.FakeRecordSet
            {
                Name = "\\052.r53pull.example.com.",
                Type = "A",
                Values = ["192.0.2.80"],
            }
        );

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);
        builder.Services.AddSingleton<IDataProtectionProvider>(
            new EphemeralDataProtectionProvider()
        );
        builder.Services.AddSingleton<CloudCredentialProtector>();
        builder.Services.AddSingleton<IZoneWriteSerializer, ZoneWriteSerializer>();
        builder.Services.AddScoped<IZoneUpdateService, ZoneUpdateService>();
        builder.Services.AddSingleton<AuthoritativeZoneCache>();
        builder.Services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<AuthoritativeZoneCache>()
        );
        builder.Services.AddScoped<
            IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            ZoneChangedEventSubscriber
        >();
        builder.Services.AddSingleton<ICloudDnsProvider, Route53DnsProvider>();
        builder.Services.AddSingleton<CloudDnsZoneSyncService>();

        _host = builder.Build();

        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        await SeedAsync(seedScope.ServiceProvider);

        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        await _fake.DisposeAsync();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    /// <summary>
    ///     This method seeds the Route 53 credential, a CloudPrimary zone with local records and a
    ///     CloudSecondary zone with only its SOA.
    /// </summary>
    private async Task SeedAsync(IServiceProvider services)
    {
        NinlilDnsContext context = services.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        IZoneRepository zoneRepository = services.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository = services.GetRequiredService<IRecordRepository>();
        ICloudDnsProviderCredentialRepository credentialRepository =
            services.GetRequiredService<ICloudDnsProviderCredentialRepository>();
        IUnitOfWork unitOfWork = services.GetRequiredService<IUnitOfWork>();
        CloudCredentialProtector protector =
            services.GetRequiredService<CloudCredentialProtector>();

        Guid credentialId = Guid.NewGuid();
        await credentialRepository.AddAsync(
            new CloudDnsProviderCredentialEntity
            {
                Id = credentialId,
                Name = "fake-route53",
                ProviderType = CloudDnsProviderTypeEnum.Route53,
                CredentialsEncrypted = protector.Protect(
                    new Dictionary<string, string>
                    {
                        ["AccessKeyId"] = "AKIAFAKE",
                        ["SecretAccessKey"] = "fake-secret",
                        ["ServiceUrl"] = _fake.Url,
                    }
                ),
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );

        Guid pushZoneId = Guid.NewGuid();
        _pullZoneId = Guid.NewGuid();

        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = pushZoneId,
                Name = "r53push.example.com",
                Type = ZoneTypeEnum.CloudPrimary,
                CloudProviderCredentialId = credentialId,
                CloudProviderZoneId = PushZoneId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _pullZoneId,
                Name = "r53pull.example.com",
                Type = ZoneTypeEnum.CloudSecondary,
                CloudProviderCredentialId = credentialId,
                CloudProviderZoneId = PullZoneId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        async Task AddRecordAsync<TData>(
            Guid zoneId,
            string name,
            DnsResourceRecordTypeEnum type,
            TData data
        )
            where TData : IDnsRecordData =>
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
                        typeof(TData),
                        data
                    ),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );

        await AddRecordAsync(
            pushZoneId,
            "www.r53push.example.com",
            DnsResourceRecordTypeEnum.A,
            new ARecordData(System.Net.IPAddress.Parse("192.0.2.10"))
        );
        await AddRecordAsync(
            pushZoneId,
            "www.r53push.example.com",
            DnsResourceRecordTypeEnum.A,
            new ARecordData(System.Net.IPAddress.Parse("192.0.2.11"))
        );
        await AddRecordAsync(
            pushZoneId,
            "mail.r53push.example.com",
            DnsResourceRecordTypeEnum.MX,
            new MxRecordData(10, DnsName.Parse("mx.r53push.example.com"))
        );
        await AddRecordAsync(
            pushZoneId,
            "*.r53push.example.com",
            DnsResourceRecordTypeEnum.A,
            new ARecordData(System.Net.IPAddress.Parse("192.0.2.12"))
        );
        await AddRecordAsync(
            _pullZoneId,
            "r53pull.example.com",
            DnsResourceRecordTypeEnum.Soa,
            new SoaRecordData(
                DnsName.Parse("ns1.r53pull.example.com"),
                DnsName.Parse("admin.r53pull.example.com"),
                1,
                3600,
                900,
                604800,
                3600
            )
        );

        await unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    ///     This method runs one sync pass over both zones on a new service instance (a stopped
    ///     <see cref="BackgroundService" /> cannot restart) and waits until both zones' sets have
    ///     been listed again, then a moment for the changes of that pass to land.
    /// </summary>
    private async Task RunOneSyncPassAsync()
    {
        int pushListings = _fake.ListRequestCount(PushZoneId);
        int pullListings = _fake.ListRequestCount(PullZoneId);

        CloudDnsZoneSyncService syncService =
            ActivatorUtilities.CreateInstance<CloudDnsZoneSyncService>(_host.Services);
        await syncService.StartAsync(CancellationToken.None);

        for (
            int attempt = 0;
            attempt < 300
                && (
                    _fake.ListRequestCount(PushZoneId) <= pushListings
                    || _fake.ListRequestCount(PullZoneId) <= pullListings
                );
            attempt++
        )
            await Task.Delay(50);

        // The changes follow the listing of the same zone; give the last zone's pull (its
        // ApplyChangesAsync and the cache reload) time to land before the service is stopped.
        await Task.Delay(1000);
        await syncService.StopAsync(CancellationToken.None);
    }

    /// <summary>This method lists the non-SOA records of the pull zone.</summary>
    private async Task<IReadOnlyList<RecordEntity>> ListPullRecordsAsync()
    {
        using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IReadOnlyList<RecordEntity> records = await recordRepository.ListByZoneIdAsync(_pullZoneId);

        return [.. records.Where(record => record.Type != DnsResourceRecordTypeEnum.Soa)];
    }

    /// <summary>
    ///     This test checks that a CloudPrimary pass publishes two local A records as one record set
    ///     with two values, an MX, and a wildcard under its octal name, and leaves the provider's apex
    ///     sets and the alias set untouched.
    /// </summary>
    [Fact]
    public async Task CloudPrimary_FirstPass_PublishesWholeSetsAndLeavesProviderSetsAlone()
    {
        await RunOneSyncPassAsync();

        List<FakeRoute53Server.FakeRecordSet> sets = _fake.Sets(PushZoneId);
        FakeRoute53Server.FakeRecordSet www = Assert.Single(
            sets,
            s => s.Name == "www.r53push.example.com." && s.Type == "A"
        );
        Assert.Equal(["192.0.2.10", "192.0.2.11"], www.Values.Order().ToArray());
        Assert.Single(sets, s => s.Name == "mail.r53push.example.com." && s.Type == "MX");
        Assert.Single(sets, s => s.Name == "\\052.r53push.example.com." && s.Type == "A");
        Assert.Equal(2, Assert.Single(sets, s => s.Type == "NS").Values.Count);
        Assert.Single(sets, s => s.Type == "SOA");
        Assert.Single(sets, s => s.AliasDnsName is not null);
    }

    /// <summary>
    ///     This test checks that a second CloudPrimary pass sends no change request: the sets it
    ///     published are read back as the same records, so nothing is re-created.
    /// </summary>
    [Fact]
    public async Task CloudPrimary_SecondPass_SendsNoChangeRequest()
    {
        await RunOneSyncPassAsync();
        int requestsAfterFirstPass = _fake.ChangeRequests(PushZoneId).Count;

        await RunOneSyncPassAsync();

        Assert.True(requestsAfterFirstPass > 0);
        Assert.Equal(requestsAfterFirstPass, _fake.ChangeRequests(PushZoneId).Count);
    }

    /// <summary>
    ///     This test checks that a CloudSecondary pass imports all three values of a TXT set and the
    ///     wildcard under its decoded owner name, and that a second pass changes nothing.
    /// </summary>
    [Fact]
    public async Task CloudSecondary_Passes_ImportEveryValueOnceAndDecodeTheWildcard()
    {
        await RunOneSyncPassAsync();
        IReadOnlyList<RecordEntity> afterFirst = await ListPullRecordsAsync();

        await RunOneSyncPassAsync();
        IReadOnlyList<RecordEntity> afterSecond = await ListPullRecordsAsync();

        Assert.Equal(
            3,
            afterFirst.Count(r =>
                r.Type == DnsResourceRecordTypeEnum.Txt && r.Name == "txt.r53pull.example.com"
            )
        );
        Assert.Single(
            afterFirst,
            r => r.Type == DnsResourceRecordTypeEnum.A && r.Name == "*.r53pull.example.com"
        );
        Assert.Equal(
            afterFirst.Select(r => r.Id).Order().ToArray(),
            afterSecond.Select(r => r.Id).Order().ToArray()
        );
    }
}
