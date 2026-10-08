using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Notify;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Notify;

/// <summary>
///     This class tests that <see cref="NotifyQueryResolver" /> recognizes a configured primary
///     when the NOTIFY arrives from it over a dual-stack socket, which reports an IPv4 sender as
///     an IPv4-mapped IPv6 address (<c>::ffff:192.0.2.1</c>). It uses a secondary zone stored in
///     a temporary SQLite database, loaded into a real <see cref="AuthoritativeZoneCache" />, and
///     a recording refresh trigger.
///     <see cref="DnsQueryContext" /> normalizes the sender address itself, so the tests build
///     contexts from the raw mapped address as the pipeline does.
/// </summary>
public sealed class NotifyQueryResolverDualStackTests : IAsyncLifetime
{
    private const string ZoneName = "secondary.example";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-notify-dualstack-tests-{Guid.NewGuid()}.sqlite3"
    );

    private readonly RecordingRefreshTrigger _trigger = new();
    private IHost _host;
    private NotifyQueryResolver _resolver;
    private Guid _zoneId;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddSingleton<AuthoritativeZoneCache>();

        _host = builder.Build();

        await using (AsyncServiceScope seedScope = _host.Services.CreateAsyncScope())
        {
            NinlilDnsContext context =
                seedScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
            await context.Database.EnsureCreatedAsync();

            _zoneId = Guid.NewGuid();
            await seedScope
                .ServiceProvider.GetRequiredService<IZoneRepository>()
                .AddAsync(
                    new ZoneEntity
                    {
                        Id = _zoneId,
                        Name = ZoneName,
                        Type = ZoneTypeEnum.Secondary,
                        PrimaryServerAddresses = ["192.0.2.1"],
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );
            await seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        AuthoritativeZoneCache cache = _host.Services.GetRequiredService<AuthoritativeZoneCache>();
        await cache.ReloadZoneAsync(_zoneId);

        _resolver = new NotifyQueryResolver(cache, _trigger);
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ANotifyFromTheConfiguredPrimaryInMappedFormIsAcknowledged()
    {
        DnsQueryResolution resolution = await _resolver.TryResolveAsync(
            BuildContext(IPAddress.Parse("::ffff:192.0.2.1"))
        );

        Assert.True(resolution.IsHandled);
        Assert.Equal(DnsResponseCodeEnum.NoError, resolution.Response.ResponseCode);
        Assert.Equal([_zoneId], _trigger.RequestedZoneIds);
    }

    [Fact]
    public async Task ANotifyFromAnotherAddressInMappedFormIsRefused()
    {
        DnsQueryResolution resolution = await _resolver.TryResolveAsync(
            BuildContext(IPAddress.Parse("::ffff:192.0.2.2"))
        );

        Assert.True(resolution.IsHandled);
        Assert.Equal(DnsResponseCodeEnum.Refused, resolution.Response.ResponseCode);
        Assert.Empty(_trigger.RequestedZoneIds);
    }

    [Fact]
    public async Task ANotifyFromTheConfiguredPrimaryInPlainFormIsStillAcknowledged()
    {
        DnsQueryResolution resolution = await _resolver.TryResolveAsync(
            BuildContext(IPAddress.Parse("192.0.2.1"))
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, resolution.Response.ResponseCode);
    }

    /// <summary>
    ///     This method builds the context of a NOTIFY for the secondary zone from a sender
    ///     address, the way the pipeline does after it receives the raw socket address.
    /// </summary>
    private static DnsQueryContext BuildContext(IPAddress senderAddress) =>
        new(
            new DnsMessage
            {
                Id = 5,
                Opcode = DnsOpcodeEnum.Notify,
                IsAuthoritativeAnswer = true,
                Questions =
                {
                    new DnsQuestion(
                        DnsName.Parse(ZoneName),
                        DnsResourceRecordTypeEnum.Soa,
                        DnsClassEnum.Internet
                    ),
                },
            },
            DnsTransportProtocolEnum.Udp,
            senderAddress,
            DateTimeOffset.UtcNow
        );

    /// <summary>This trigger records the zones a refresh was requested for.</summary>
    private sealed class RecordingRefreshTrigger : ISecondaryZoneRefreshTrigger
    {
        public List<Guid> RequestedZoneIds { get; } = [];

        public void RequestImmediateRefresh(Guid zoneId) => RequestedZoneIds.Add(zoneId);
    }
}
