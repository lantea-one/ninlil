using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Xunit;

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration.Tests;

/// <summary>
///     This class covers <see cref="DhcpLeaseClientIdentityResolver" /> on an
///     <see cref="ILeaseRepository" /> over a temporary SQLite file: a source address resolves to
///     the identifier of the active lease holding it, in any scope, and inactive leases and
///     unknown addresses resolve to nothing.
/// </summary>
public sealed class DhcpLeaseClientIdentityResolverTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dhcp-lease-client-identity-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder
            .Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration)
            .AddNinlilDhcpEntityFrameworkCore();
        builder.Services.AddSingleton<DhcpLeaseClientIdentityResolver>();

        _host = builder.Build();

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ResolveAsync_WithAnAddressHeldByAnActiveDhcpv4Lease_ReturnsItsMacAddress()
    {
        await using (AsyncServiceScope seedScope = _host.Services.CreateAsyncScope())
        {
            ILeaseRepository leaseRepository =
                seedScope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IUnitOfWork unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = "resolver-test-scope",
                    ClientIdentifier = "aa:bb:cc:dd:ee:ff",
                    IpAddress = "192.0.2.77",
                    State = LeaseStateEnum.Active,
                }
            );
            await unitOfWork.SaveChangesAsync();
        }

        DhcpLeaseClientIdentityResolver resolver =
            _host.Services.GetRequiredService<DhcpLeaseClientIdentityResolver>();

        ClientIdentityResolution resolution = await resolver.ResolveAsync(
            IPAddress.Parse("192.0.2.77")
        );

        Assert.NotNull(resolution);
        Assert.Equal("aa:bb:cc:dd:ee:ff", resolution.ClientIdentifier);
        Assert.Equal(InternetProtocolVersionEnum.IPv4, resolution.ProtocolVersion);
    }

    [Fact]
    public async Task ResolveAsync_WithAnAddressHeldByAnActiveDhcpv6Lease_ReturnsItsDuid()
    {
        await using (AsyncServiceScope seedScope = _host.Services.CreateAsyncScope())
        {
            ILeaseRepository leaseRepository =
                seedScope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IUnitOfWork unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv6,
                    ScopeName = "resolver-test-scope-v6",
                    ClientIdentifier = "00:03:00:01:aa:bb:cc:dd:ee:ff",
                    IpAddress = "2001:db8::42",
                    State = LeaseStateEnum.Active,
                }
            );
            await unitOfWork.SaveChangesAsync();
        }

        DhcpLeaseClientIdentityResolver resolver =
            _host.Services.GetRequiredService<DhcpLeaseClientIdentityResolver>();

        ClientIdentityResolution resolution = await resolver.ResolveAsync(
            IPAddress.Parse("2001:db8::42")
        );

        Assert.NotNull(resolution);
        Assert.Equal("00:03:00:01:aa:bb:cc:dd:ee:ff", resolution.ClientIdentifier);
        Assert.Equal(InternetProtocolVersionEnum.IPv6, resolution.ProtocolVersion);
    }

    [Fact]
    public async Task ResolveAsync_WithAnAddressHeldOnlyByAReleasedLease_ReturnsNull()
    {
        await using (AsyncServiceScope seedScope = _host.Services.CreateAsyncScope())
        {
            ILeaseRepository leaseRepository =
                seedScope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IUnitOfWork unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = "resolver-test-scope",
                    ClientIdentifier = "11:22:33:44:55:66",
                    IpAddress = "192.0.2.88",
                    State = LeaseStateEnum.Released,
                }
            );
            await unitOfWork.SaveChangesAsync();
        }

        DhcpLeaseClientIdentityResolver resolver =
            _host.Services.GetRequiredService<DhcpLeaseClientIdentityResolver>();

        ClientIdentityResolution resolution = await resolver.ResolveAsync(
            IPAddress.Parse("192.0.2.88")
        );

        Assert.Null(resolution);
    }

    [Fact]
    public async Task ResolveAsync_WithAnAddressNoLeaseEverHeld_ReturnsNull()
    {
        DhcpLeaseClientIdentityResolver resolver =
            _host.Services.GetRequiredService<DhcpLeaseClientIdentityResolver>();

        ClientIdentityResolution resolution = await resolver.ResolveAsync(
            IPAddress.Parse("192.0.2.99")
        );

        Assert.Null(resolution);
    }

    /// <summary>
    ///     This test checks that an IPv4-mapped IPv6 source address ("::ffff:x.x.x.x"), which is
    ///     how IPv4 clients arrive from the dual-stack DNS listeners, matches a lease stored in
    ///     dotted-quad form in <see cref="LeaseEntity.IpAddress" />. Without unmapping in
    ///     <see cref="DhcpLeaseClientIdentityResolver.ResolveAsync" />, no IPv4 query would ever
    ///     resolve.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WithAnIPv4MappedIPv6Address_ResolvesTheSameAsItsPlainIPv4Form()
    {
        await using (AsyncServiceScope seedScope = _host.Services.CreateAsyncScope())
        {
            ILeaseRepository leaseRepository =
                seedScope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IUnitOfWork unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = "resolver-test-scope",
                    ClientIdentifier = "77:88:99:aa:bb:cc",
                    IpAddress = "192.0.2.123",
                    State = LeaseStateEnum.Active,
                }
            );
            await unitOfWork.SaveChangesAsync();
        }

        DhcpLeaseClientIdentityResolver resolver =
            _host.Services.GetRequiredService<DhcpLeaseClientIdentityResolver>();

        ClientIdentityResolution resolution = await resolver.ResolveAsync(
            IPAddress.Parse("192.0.2.123").MapToIPv6()
        );

        Assert.NotNull(resolution);
        Assert.Equal("77:88:99:aa:bb:cc", resolution.ClientIdentifier);
        Assert.Equal(InternetProtocolVersionEnum.IPv4, resolution.ProtocolVersion);
    }
}
