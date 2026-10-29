using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Update;
using Ninlil.Dns.Server.Core.Resolution;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Update;

/// <summary>
///     This class tests the checks <see cref="DynamicUpdateQueryResolver" /> makes before TSIG and
///     the prerequisites (RFC 2136 §3.1): the Zone Section, whether the zone is hosted and
///     whether updates are enabled. It uses the real <see cref="AuthoritativeZoneCache" /> over a
///     temporary SQLite database and calls the resolver directly, so no pipeline or socket is
///     involved; the signed paths are covered by <see cref="SignedMessagePipelineTests" />.
/// </summary>
public sealed class DynamicUpdateQueryResolverTests : IAsyncLifetime
{
    private const string EnabledZone = "enabled.example";
    private const string DisabledZone = "disabled.example";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-update-resolver-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private AuthoritativeZoneCache _zoneCache;
    private DynamicUpdateQueryResolver _resolver;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        _host = builder.Build();

        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<NinlilDnsContext>()
                .Database.EnsureCreatedAsync();
            IZoneRepository zones = scope.ServiceProvider.GetRequiredService<IZoneRepository>();

            foreach (
                (string name, bool enabled) in new[] { (EnabledZone, true), (DisabledZone, false) }
            )
                await zones.AddAsync(
                    new ZoneEntity
                    {
                        Id = Guid.NewGuid(),
                        Name = name,
                        Type = ZoneTypeEnum.Primary,
                        DynamicUpdateEnabled = enabled,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        _zoneCache = new AuthoritativeZoneCache(
            _host.Services.GetRequiredService<IServiceScopeFactory>()
        );
        await _zoneCache.StartAsync(CancellationToken.None);
        _resolver = new DynamicUpdateQueryResolver(
            _zoneCache,
            _host.Services.GetRequiredService<IServiceScopeFactory>()
        );
    }

    public async Task DisposeAsync()
    {
        await _zoneCache.StopAsync(CancellationToken.None);
        _host.Dispose();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
    }

    private static DnsQuestion Soa(string name) =>
        new(DnsName.Parse(name), DnsResourceRecordTypeEnum.Soa, DnsClassEnum.Internet);

    private async Task<DnsResponseCodeEnum> ResolveAsync(params DnsQuestion[] questions)
    {
        DnsMessage request = new() { Id = 7, Opcode = DnsOpcodeEnum.Update };

        foreach (DnsQuestion question in questions)
            request.Questions.Add(question);

        DnsQueryResolution resolution = await _resolver.TryResolveAsync(
            new DnsQueryContext(
                request,
                DnsTransportProtocolEnum.Udp,
                IPAddress.Loopback,
                DateTimeOffset.UtcNow
            )
        );

        Assert.NotNull(resolution.Response);
        return resolution.Response.ResponseCode;
    }

    [Fact]
    public async Task TryResolve_WithTwoZoneSectionEntries_ReturnsFormatError()
    {
        Assert.Equal(
            DnsResponseCodeEnum.FormatError,
            await ResolveAsync(Soa(EnabledZone), Soa(EnabledZone))
        );
    }

    [Fact]
    public async Task TryResolve_WithNoZoneSectionEntry_ReturnsFormatError()
    {
        Assert.Equal(DnsResponseCodeEnum.FormatError, await ResolveAsync());
    }

    [Fact]
    public async Task TryResolve_ForAZoneThisServerDoesNotHost_ReturnsNotAuth()
    {
        Assert.Equal(
            DnsResponseCodeEnum.NotAuthoritative,
            await ResolveAsync(Soa("unhosted.example.net"))
        );
    }

    [Fact]
    public async Task TryResolve_ForANameBelowTheApex_ReturnsNotAuth()
    {
        Assert.Equal(
            DnsResponseCodeEnum.NotAuthoritative,
            await ResolveAsync(Soa($"sub.{EnabledZone}"))
        );
    }

    [Fact]
    public async Task TryResolve_ForAZoneWithUpdatesDisabled_ReturnsRefused()
    {
        Assert.Equal(DnsResponseCodeEnum.Refused, await ResolveAsync(Soa(DisabledZone)));
    }

    [Fact]
    public async Task TryResolve_ForAnEnabledZoneWithoutAVerifiedSignature_ReturnsRefused()
    {
        Assert.Equal(DnsResponseCodeEnum.Refused, await ResolveAsync(Soa(EnabledZone)));
    }
}
