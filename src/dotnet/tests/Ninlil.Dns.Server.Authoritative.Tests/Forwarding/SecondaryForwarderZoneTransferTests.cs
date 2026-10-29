using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Protocol.Plain;
using Ninlil.Dns.Protocol.Plain.Extension;
using Ninlil.Dns.Server.Authoritative.Extension;
using Ninlil.Dns.Server.Authoritative.Forwarding;
using Ninlil.Dns.Server.Authoritative.Refresh;
using Ninlil.Dns.Server.Authoritative.Resolution;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Extension;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Dnssec.Extension;
using Ninlil.Dns.Server.Tsig.Extension;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Forwarding;

/// <summary>
///     This class tests a <see cref="ZoneTypeEnum.Forwarder" /> zone with two upstream addresses on
///     one host, whose apex SOA and TXT records <see cref="ForwarderZoneContentSyncService" />
///     maintains, being pulled over loopback TCP by a <see cref="ZoneTypeEnum.SecondaryForwarder" />
///     zone on a second host with its own SQLite database and
///     <see cref="SecondaryZoneRefreshOptions" />-driven poll, and the forwarder addresses
///     arriving.
/// </summary>
public sealed class SecondaryForwarderZoneTransferTests : IAsyncLifetime
{
    private const string ZoneName = "forward-me.example.com";
    private const string TsigKeyName = "forwarder-transfer-test-key";

    private static readonly IReadOnlyList<string> ConfiguredForwarderAddresses =
    [
        "203.0.113.10:53",
        "tls://203.0.113.20:853",
    ];

    private readonly string _primaryDatabasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-secondaryforwarder-primary-{Guid.NewGuid()}.sqlite3"
    );
    private readonly string _secondaryDatabasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-secondaryforwarder-secondary-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _primaryHost;
    private IHost _secondaryHost;
    private Guid _primaryZoneId;
    private Guid _secondaryZoneId;

    public async Task InitializeAsync()
    {
        int tcpPort = GetFreeTcpPort();
        byte[] rawTsigSecret = new byte[32];
        Random.Shared.NextBytes(rawTsigSecret);

        _primaryHost = BuildPrimaryHost(tcpPort);
        await SeedPrimaryAsync(rawTsigSecret);

        // Starts AuthoritativeZoneCache's initial load and TcpDnsListener's real socket
        // bind, so the secondary host below has a real, already-listening primary to pull from
        // the moment it starts.
        await _primaryHost.StartAsync();
        await WaitUntilPrimaryTcpListenerIsBoundAsync();

        _secondaryHost = BuildSecondaryHost(tcpPort);
        await SeedSecondaryAsync(rawTsigSecret);

        // SecondaryZoneRefreshService's ExecuteAsync checks every due zone once, immediately,
        // before its first poll-interval sleep -- a zone with no prior check recorded is always
        // due, so starting this host alone triggers the real pull this class's tests observe.
        await _secondaryHost.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_secondaryHost is not null)
        {
            await _secondaryHost.StopAsync();
            _secondaryHost.Dispose();
        }

        if (_primaryHost is not null)
        {
            await _primaryHost.StopAsync();
            _primaryHost.Dispose();
        }

        TemporaryFiles.DeleteSqliteDatabase(_primaryDatabasePath);
        TemporaryFiles.DeleteSqliteDatabase(_secondaryDatabasePath);
    }

    [Fact]
    public async Task ThePrimarysOwnForwarderZoneGetsARealSynthenticSoaAndOneTxtRecordPerAddress()
    {
        await using AsyncServiceScope scope = _primaryHost.Services.CreateAsyncScope();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();

        IReadOnlyList<RecordEntity> records = await recordRepository.ListByZoneIdAsync(
            _primaryZoneId
        );

        Assert.Contains(records, record => record.Type == DnsResourceRecordTypeEnum.Soa);

        List<RecordEntity> txtRecords =
        [
            .. records.Where(record => record.Type == DnsResourceRecordTypeEnum.Txt),
        ];
        Assert.Equal(ConfiguredForwarderAddresses.Count, txtRecords.Count);
        Assert.All(
            txtRecords,
            record =>
                Assert.Equal(RecordManagedByFeatureEnum.ForwarderAddress, record.ManagedByFeature)
        );
    }

    [Fact]
    public async Task TheSecondaryForwarderZonePullsAndDecodesTheExactConfiguredAddressListInOrder()
    {
        ZoneEntity secondaryZone = await PollUntilAsync(
            async () =>
            {
                await using AsyncServiceScope scope = _secondaryHost.Services.CreateAsyncScope();
                IZoneRepository zoneRepository =
                    scope.ServiceProvider.GetRequiredService<IZoneRepository>();

                return await zoneRepository.GetByIdAsync(_secondaryZoneId);
            },
            zone => zone.ForwarderAddresses.Count == ConfiguredForwarderAddresses.Count
        );

        // Both the content and this project's list *order* (this default failover strategy's
        // own notion of priority) must survive the real TXT-encode/AXFR-transfer/TXT-decode round
        // trip -- proving ForwarderAddressTxtCodec's index prefix did its
        // job rather than relying on however the storage engine or wire transfer happened to order
        // the underlying TXT RRset.
        Assert.Equal(ConfiguredForwarderAddresses, secondaryZone.ForwarderAddresses);
    }

    [Fact]
    public async Task AuthoritativeQueryResolverOnlyEverAnswersTheApexSoaForAForwarderZone()
    {
        // Wait for the secondary's pull to land first, so this test proves
        // the *steady-state* behavior rather than a race against a pull still in flight.
        await PollUntilAsync(
            async () =>
            {
                await using AsyncServiceScope scope = _secondaryHost.Services.CreateAsyncScope();
                IZoneRepository zoneRepository =
                    scope.ServiceProvider.GetRequiredService<IZoneRepository>();

                return await zoneRepository.GetByIdAsync(_secondaryZoneId);
            },
            zone => zone.ForwarderAddresses.Count == ConfiguredForwarderAddresses.Count
        );

        AuthoritativeQueryResolver resolver = _primaryHost
            .Services.GetServices<IDnsQueryResolver>()
            .OfType<AuthoritativeQueryResolver>()
            .Single();

        // An ordinary A-record query under the Forwarder zone's name is declined here, so
        // IDnsQueryDispatcher still falls through to Server.Forwarding's ForwarderQueryResolver
        // as it always has.
        DnsQueryResolution ordinaryQueryResolution = await resolver.TryResolveAsync(
            new DnsQueryContext(
                BuildQuery($"www.{ZoneName}.", DnsResourceRecordTypeEnum.A),
                DnsTransportProtocolEnum.Udp,
                IPAddress.Loopback,
                DateTimeOffset.UtcNow
            )
        );

        // A query for the zone's apex TXT content itself (the very records that carry its
        // configured forwarder addresses) is declined too -- that content is reachable only via
        // an authenticated AXFR/IXFR pull, never an ordinary, unauthenticated query.
        DnsQueryResolution apexTxtResolution = await resolver.TryResolveAsync(
            new DnsQueryContext(
                BuildQuery($"{ZoneName}.", DnsResourceRecordTypeEnum.Txt),
                DnsTransportProtocolEnum.Udp,
                IPAddress.Loopback,
                DateTimeOffset.UtcNow
            )
        );

        // The one narrow exception: an ordinary SOA query at the exact apex *is* answered here --
        // Refresh.SecondaryZoneRefreshService's freshness pre-check (an ordinary, unsigned SOA
        // query sent before ever attempting a real, TSIG-gated AXFR/IXFR pull) depends on it, and
        // a bare SOA record carries no sensitive forwarder-address content to leak.
        DnsQueryResolution apexSoaResolution = await resolver.TryResolveAsync(
            new DnsQueryContext(
                BuildQuery($"{ZoneName}.", DnsResourceRecordTypeEnum.Soa),
                DnsTransportProtocolEnum.Udp,
                IPAddress.Loopback,
                DateTimeOffset.UtcNow
            )
        );

        Assert.False(ordinaryQueryResolution.IsHandled);
        Assert.False(apexTxtResolution.IsHandled);
        Assert.True(apexSoaResolution.IsHandled);
        Assert.Equal(DnsResponseCodeEnum.NoError, apexSoaResolution.Response.ResponseCode);
    }

    private static DnsMessage BuildQuery(string name, DnsResourceRecordTypeEnum type)
    {
        DnsMessage message = new() { Id = 1, Opcode = DnsOpcodeEnum.Query };
        message.Questions.Add(new DnsQuestion(DnsName.Parse(name), type, DnsClassEnum.Internet));
        return message;
    }

    /// <summary>
    ///     This method polls <paramref name="read" /> until <paramref name="isDone" /> is
    ///     satisfied or a generous timeout elapses, standing in for a real operator watching a
    ///     background poll loop complete rather than asserting against a fixed, flaky
    ///     sleep.
    /// </summary>
    private static async Task<ZoneEntity> PollUntilAsync(
        Func<Task<ZoneEntity>> read,
        Func<ZoneEntity, bool> isDone
    )
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        ZoneEntity last = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await read();

            if (last is not null && isDone(last))
                return last;

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        Assert.Fail(
            $"Timed out waiting for the SecondaryForwarder zone's own ForwarderAddresses to "
                + $"reach the expected state. Last observed: "
                + $"[{(last is null ? "<zone not found>" : string.Join(", ", last.ForwarderAddresses))}]."
        );
        return last;
    }

    /// <summary>
    ///     This method waits for the primary host's real <see cref="TcpDnsListener" /> to
    ///     report it has bound its socket -- <see cref="BackgroundService.StartAsync" />
    ///     (which <see cref="IHost.StartAsync" /> does await) only starts <see cref="TcpDnsListener.ExecuteAsync" />
    ///     running in the background rather than waiting for its first statements (the real
    ///     socket bind) to complete, so this closes that small window rather than leaving the
    ///     secondary host's very first pull attempt to race it.
    /// </summary>
    private async Task WaitUntilPrimaryTcpListenerIsBoundAsync()
    {
        TcpDnsListener listener = _primaryHost
            .Services.GetServices<IHostedService>()
            .OfType<TcpDnsListener>()
            .Single();

        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);

        while (!listener.IsListening && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(TimeSpan.FromMilliseconds(20));

        Assert.True(listener.IsListening, "The primary host's own TcpDnsListener never bound.");
    }

    /// <summary>
    ///     This method finds a free loopback TCP port by briefly binding one and releasing it --
    ///     good enough for a single-process test run, the same technique this project's test
    ///     suite already trusts for <c>FakeUdpUpstream</c>'s port selection.
    /// </summary>
    private static int GetFreeTcpPort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private IHost BuildPrimaryHost(int tcpPort)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_primaryDatabasePath}";
        builder.Configuration["DnsServer:Protocol:Plain:Port"] = tcpPort.ToString();

        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);
        builder.Services.AddSingleton<IDataProtectionProvider>(
            new EphemeralDataProtectionProvider()
        );
        builder.Services.AddNinlilDnsServerTsig(builder.Configuration);
        builder.Services.AddNinlilDnsServerDnssec();
        builder.Services.AddNinlilDnsServerAuthoritative(builder.Configuration);
        builder.Services.AddNinlilDnsProtocolPlain(builder.Configuration);

        return builder.Build();
    }

    private IHost BuildSecondaryHost(int primaryTcpPort)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_secondaryDatabasePath}";
        // Points this secondary's IZoneTransferClient at the primary's real loopback TCP
        // port instead of the real, privileged, port-53 default -- ZoneTransferClientOptions.PrimaryPort's
        // own doc comment names this exact scenario as why it is configurable at all.
        builder.Configuration["DnsServer:Authoritative:ZoneTransferClient:PrimaryPort"] =
            primaryTcpPort.ToString();
        // A short poll interval so a retry (should the very first attempt race the primary's own
        // startup) happens quickly rather than this test needing to wait out the 15-second default.
        builder.Configuration["DnsServer:Authoritative:SecondaryZoneRefresh:PollInterval"] =
            "00:00:01";
        builder.Configuration["DnsServer:Authoritative:SecondaryZoneRefresh:DefaultRetryInterval"] =
            "00:00:01";

        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);
        builder.Services.AddSingleton<IDataProtectionProvider>(
            new EphemeralDataProtectionProvider()
        );
        builder.Services.AddNinlilDnsServerTsig(builder.Configuration);
        builder.Services.AddNinlilDnsServerDnssec();
        builder.Services.AddNinlilDnsServerAuthoritative(builder.Configuration);

        return builder.Build();
    }

    private async Task SeedPrimaryAsync(byte[] rawTsigSecret)
    {
        await using AsyncServiceScope scope = _primaryHost.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        ForwarderZoneContentSyncService syncService =
            scope.ServiceProvider.GetRequiredService<ForwarderZoneContentSyncService>();

        _primaryZoneId = Guid.NewGuid();

        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _primaryZoneId,
                Name = ZoneName,
                Type = ZoneTypeEnum.Forwarder,
                ForwarderAddresses = [.. ConfiguredForwarderAddresses],
                TsigKeyName = TsigKeyName,
                TransferTsigKeyNames = [TsigKeyName],
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        await SeedTsigKeyAsync(scope.ServiceProvider, rawTsigSecret);

        // In real deployment this reconciliation happens automatically, off the event bus, the
        // moment ZonesController publishes DnsZoneChangedEvent for this zone's creation --
        // called directly here since this test seeds the zone straight through the repository,
        // bypassing the REST layer entirely, like ZoneTransferServiceDnssecTests already
        // does for its zone/record seeding.
        await syncService.ReconcileZoneAsync(_primaryZoneId);
    }

    private async Task SeedSecondaryAsync(byte[] rawTsigSecret)
    {
        await using AsyncServiceScope scope = _secondaryHost.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        _secondaryZoneId = Guid.NewGuid();

        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = _secondaryZoneId,
                Name = ZoneName,
                Type = ZoneTypeEnum.SecondaryForwarder,
                PrimaryServerAddresses = ["127.0.0.1"],
                TsigKeyName = TsigKeyName,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        // The same TSIG key name and secret as the primary -- a real deployment's operator
        // configures both sides with the matching key by hand; each side still encrypts its own
        // copy of the shared secret at rest with its own, independent Data Protection key ring
        // (the ephemeral-protector gotcha only governs sharing one *protector instance* within one process,
        // not the plaintext secret two different processes must independently agree on).
        await SeedTsigKeyAsync(scope.ServiceProvider, rawTsigSecret);
    }

    private static async Task SeedTsigKeyAsync(IServiceProvider services, byte[] rawTsigSecret)
    {
        IDataProtectionProvider dataProtectionProvider =
            services.GetRequiredService<IDataProtectionProvider>();
        IDataProtector protector = dataProtectionProvider.CreateProtector(
            "Ninlil.Dns.Server.Tsig.SharedSecret"
        );

        ITsigKeyRepository tsigKeyRepository = services.GetRequiredService<ITsigKeyRepository>();
        IUnitOfWork unitOfWork = services.GetRequiredService<IUnitOfWork>();

        await tsigKeyRepository.AddAsync(
            new TsigKeyEntity
            {
                Id = Guid.NewGuid(),
                Name = TsigKeyName,
                AlgorithmName = "hmac-sha256.",
                SecretEncrypted = protector.Protect(Convert.ToBase64String(rawTsigSecret)),
                IsDisabled = false,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();
    }
}
