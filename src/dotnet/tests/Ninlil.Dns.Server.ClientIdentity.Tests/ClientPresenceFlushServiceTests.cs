using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.ClientIdentity.Presence;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests;

/// <summary>
///     This class covers <see cref="ClientPresenceTracker" /> and
///     <see cref="ClientPresenceFlushService" /> together against a temporary SQLite database: each
///     sighting kind reaching the right stored address, family-safe address updates, source
///     addresses resolved through <see cref="IClientIdentityResolver" />, and sightings that must
///     never move presence backwards or touch unknown devices.
/// </summary>
public sealed class ClientPresenceFlushServiceTests : IAsyncLifetime
{
    /// <summary>A sample MAC address.</summary>
    private const string Mac = "aa:bb:cc:dd:ee:01";

    /// <summary>The temporary database file for this test.</summary>
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-presence-tests-{Guid.NewGuid()}.sqlite3"
    );

    /// <summary>The resolver source-address sightings are resolved through.</summary>
    private readonly FakeResolver _resolver = new();

    /// <summary>The tracker sightings are recorded into.</summary>
    private readonly ClientPresenceTracker _tracker = new();

    /// <summary>The host providing the Entity Framework repositories.</summary>
    private IHost _host;

    /// <summary>The service under test.</summary>
    private ClientPresenceFlushService _flush;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddSingleton<IClientIdentityResolver>(_resolver);
        _host = builder.Build();

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<NinlilDnsContext>()
            .Database.EnsureCreatedAsync();

        _flush = new ClientPresenceFlushService(
            _tracker,
            _host.Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<ClientPresenceOptions>(new ClientPresenceOptions()),
            NullLogger<ClientPresenceFlushService>.Instance
        );
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        _host.Dispose();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    /// <summary>
    ///     This method registers a device with the given addresses and returns their row ids.
    /// </summary>
    private async Task<Guid[]> SeedDeviceAsync(
        params (string Identifier, InternetProtocolVersionEnum Version)[] identities
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow.AddDays(-1);
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        DnsClientEntity device = new() { Id = Guid.NewGuid(), CreatedAtUtc = now };
        await scope.ServiceProvider.GetRequiredService<IDnsClientRepository>().AddAsync(device);

        List<Guid> ids = [];
        foreach ((string identifier, InternetProtocolVersionEnum version) in identities)
        {
            DnsClientAddressEntity address = new()
            {
                Id = Guid.NewGuid(),
                ClientId = device.Id,
                ClientIdentifier = identifier,
                ProtocolVersion = version,
                FirstSeenUtc = now,
                CreatedAtUtc = now,
            };
            await scope
                .ServiceProvider.GetRequiredService<IDnsClientAddressRepository>()
                .AddAsync(address);
            ids.Add(address.Id);
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return [.. ids];
    }

    /// <summary>
    ///     This method reads one address row back through a fresh scope.
    /// </summary>
    private async Task<DnsClientAddressEntity> ReadAsync(Guid id)
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDnsClientAddressRepository>()
            .GetByIdAsync(id);
    }

    /// <summary>
    ///     This test checks that an identity sighting advances its address row's presence and address,
    ///     matching the identifier case-insensitively.
    /// </summary>
    [Fact]
    public async Task AnIdentitySightingAdvancesItsAddress()
    {
        Guid[] ids = await SeedDeviceAsync((Mac, InternetProtocolVersionEnum.IPv4));
        DateTimeOffset seen = DateTimeOffset.UtcNow;

        _tracker.RecordIdentity(
            Mac.ToUpperInvariant(),
            InternetProtocolVersionEnum.IPv4,
            IPAddress.Parse("192.0.2.20"),
            seen
        );

        Assert.Equal(1, await _flush.FlushAsync());
        DnsClientAddressEntity row = await ReadAsync(ids[0]);
        Assert.Equal(seen, row.LastSeenUtc);
        Assert.Equal("192.0.2.20", row.Address);
    }

    /// <summary>
    ///     This test checks that a neighbor-table MAC sighting reaches both of a dual-stack device's
    ///     addresses (the IPv4 MAC and an IPv6 DUID-LL embedding it), while its IPv4 address is
    ///     recorded only on the IPv4 row.
    /// </summary>
    [Fact]
    public async Task AHardwareSightingReachesEveryAddressEmbeddingThatMac()
    {
        Guid[] ids = await SeedDeviceAsync(
            (Mac, InternetProtocolVersionEnum.IPv4),
            ("00:03:00:01:" + Mac, InternetProtocolVersionEnum.IPv6)
        );
        DateTimeOffset seen = DateTimeOffset.UtcNow;

        _tracker.RecordHardwareAddress(
            Convert.FromHexString("aabbccddee01"),
            IPAddress.Parse("192.0.2.21"),
            seen
        );

        Assert.Equal(2, await _flush.FlushAsync());
        DnsClientAddressEntity v4 = await ReadAsync(ids[0]);
        DnsClientAddressEntity v6 = await ReadAsync(ids[1]);
        Assert.Equal(seen, v4.LastSeenUtc);
        Assert.Equal("192.0.2.21", v4.Address);
        Assert.Equal(seen, v6.LastSeenUtc);
        Assert.Null(v6.Address);
    }

    /// <summary>
    ///     This test checks that a MAC seen over both IPv4 and IPv6, with the IPv6 sighting newer,
    ///     still gives the IPv4 identity its IPv4 address and the IPv6 identity its IPv6 address,
    ///     rather than the newest sighting shadowing the other family.
    /// </summary>
    [Fact]
    public async Task ADualStackHardwareSightingGivesEachFamilyItsOwnAddress()
    {
        Guid[] ids = await SeedDeviceAsync(
            (Mac, InternetProtocolVersionEnum.IPv4),
            ("00:03:00:01:" + Mac, InternetProtocolVersionEnum.IPv6)
        );
        byte[] mac = Convert.FromHexString("aabbccddee01");
        DateTimeOffset seen = DateTimeOffset.UtcNow;

        _tracker.RecordHardwareAddress(mac, IPAddress.Parse("192.0.2.23"), seen);
        _tracker.RecordHardwareAddress(mac, IPAddress.Parse("2001:db8::23"), seen.AddSeconds(1));

        Assert.Equal(2, await _flush.FlushAsync());
        DnsClientAddressEntity v4 = await ReadAsync(ids[0]);
        DnsClientAddressEntity v6 = await ReadAsync(ids[1]);
        Assert.Equal("192.0.2.23", v4.Address);
        Assert.Equal(seen.AddSeconds(1), v4.LastSeenUtc);
        Assert.Equal("2001:db8::23", v6.Address);
    }

    /// <summary>
    ///     This test checks that a DNS source-address sighting, including one in IPv4-mapped form
    ///     from a dual-stack listener, is resolved to its identity at flush time.
    /// </summary>
    [Fact]
    public async Task ASourceAddressSightingIsResolvedAtFlushTime()
    {
        Guid[] ids = await SeedDeviceAsync((Mac, InternetProtocolVersionEnum.IPv4));
        _resolver.Map(IPAddress.Parse("192.0.2.22"), Mac, InternetProtocolVersionEnum.IPv4);
        DateTimeOffset seen = DateTimeOffset.UtcNow;

        _tracker.RecordSourceAddress(IPAddress.Parse("::ffff:192.0.2.22"), seen);

        Assert.Equal(1, await _flush.FlushAsync());
        DnsClientAddressEntity row = await ReadAsync(ids[0]);
        Assert.Equal(seen, row.LastSeenUtc);
        Assert.Equal("192.0.2.22", row.Address);
    }

    /// <summary>
    ///     This test checks that an older sighting never moves presence backwards.
    /// </summary>
    [Fact]
    public async Task AnOlderSightingNeverMovesPresenceBackwards()
    {
        Guid[] ids = await SeedDeviceAsync((Mac, InternetProtocolVersionEnum.IPv4));
        DateTimeOffset newer = DateTimeOffset.UtcNow;

        _tracker.RecordIdentity(Mac, InternetProtocolVersionEnum.IPv4, null, newer);
        await _flush.FlushAsync();
        _tracker.RecordIdentity(Mac, InternetProtocolVersionEnum.IPv4, null, newer.AddMinutes(-5));

        Assert.Equal(0, await _flush.FlushAsync());
        Assert.Equal(newer, (await ReadAsync(ids[0])).LastSeenUtc);
    }

    /// <summary>
    ///     This test checks that when the tracker refused source addresses because it was full,
    ///     the flush logs one warning with the count.
    /// </summary>
    [Fact]
    public async Task AFlushAfterTheSourceBufferOverflowed_LogsOneWarningWithTheDropCount()
    {
        RecordingLogger logger = new();
        ClientPresenceFlushService flush = new(
            _tracker,
            _host.Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<ClientPresenceOptions>(new ClientPresenceOptions()),
            logger
        );
        for (int i = 0; i < ClientPresenceTracker.MaxSourceAddressesPerFlush + 7; i++)
            _tracker.RecordSourceAddress(
                new IPAddress([10, (byte)(i >> 16), (byte)(i >> 8), (byte)i]),
                DateTimeOffset.UtcNow
            );

        await flush.FlushAsync();

        (LogLevel level, string message) = Assert.Single(logger.Messages);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("7 new source addresses", message);
    }

    /// <summary>
    ///     This class keeps every message logged to it, rendered.
    /// </summary>
    private sealed class RecordingLogger : ILogger<ClientPresenceFlushService>
    {
        public List<(LogLevel, string)> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Messages.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>
    ///     This test checks that a sighting of an unregistered device changes nothing.
    /// </summary>
    [Fact]
    public async Task ASightingOfAnUnknownDeviceChangesNothing()
    {
        Guid[] ids = await SeedDeviceAsync((Mac, InternetProtocolVersionEnum.IPv4));

        _tracker.RecordIdentity(
            "11:22:33:44:55:66",
            InternetProtocolVersionEnum.IPv4,
            null,
            DateTimeOffset.UtcNow
        );

        Assert.Equal(0, await _flush.FlushAsync());
        Assert.Null((await ReadAsync(ids[0])).LastSeenUtc);
    }

    /// <summary>
    ///     This test checks that the tracker keeps only the most recent sighting per key, regardless of
    ///     recording order.
    /// </summary>
    [Fact]
    public void TheTrackerCoalescesToTheMostRecentSighting()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _tracker.RecordIdentity(Mac, InternetProtocolVersionEnum.IPv4, null, now);
        _tracker.RecordIdentity(Mac, InternetProtocolVersionEnum.IPv4, null, now.AddMinutes(-1));

        ClientPresenceSnapshot snapshot = _tracker.Drain();

        Assert.Equal(now, Assert.Single(snapshot.Identities).Value.SeenAtUtc);
        Assert.True(_tracker.Drain().IsEmpty);
    }

    /// <summary>
    ///     This test checks that the online window is applied at read time.
    /// </summary>
    [Fact]
    public void OnlineIsInferredFromTheWindow()
    {
        ClientPresenceOptions options = new() { OnlineWindow = TimeSpan.FromMinutes(10) };
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Assert.True(options.IsOnline(now.AddMinutes(-9), now));
        Assert.False(options.IsOnline(now.AddMinutes(-11), now));
        Assert.False(options.IsOnline(null, now));
    }

    /// <summary>
    ///     This class is a fake <see cref="IClientIdentityResolver" /> that resolves addresses from a
    ///     fixed map.
    /// </summary>
    private sealed class FakeResolver : IClientIdentityResolver
    {
        /// <summary>The address-to-identity map.</summary>
        private readonly Dictionary<IPAddress, ClientIdentityResolution> _map = [];

        /// <summary>Adds a mapping.</summary>
        public void Map(
            IPAddress address,
            string identifier,
            InternetProtocolVersionEnum version
        ) => _map[address] = new ClientIdentityResolution(identifier, version);

        /// <inheritdoc />
        public Task<ClientIdentityResolution> ResolveAsync(
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(_map.GetValueOrDefault(remoteAddress));
    }
}
