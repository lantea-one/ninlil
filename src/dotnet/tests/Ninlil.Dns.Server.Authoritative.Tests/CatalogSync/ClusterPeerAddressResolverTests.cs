using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.CatalogSync;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.CatalogSync;

/// <summary>
///     This class tests <see cref="ClusterPeerAddressResolver" /> against the EF Core
///     <see cref="IClusterNodeRepository" /> on a temporary SQLite database.
/// </summary>
public sealed class ClusterPeerAddressResolverTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-cluster-peer-resolver-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private IClusterNodeRepository _clusterNodeRepository;
    private IUnitOfWork _unitOfWork;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);

        _host = builder.Build();

        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = seedScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        _clusterNodeRepository = _host.Services.GetRequiredService<IClusterNodeRepository>();
        _unitOfWork = _host.Services.GetRequiredService<IUnitOfWork>();
    }

    public Task DisposeAsync()
    {
        _host.Dispose();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);

        return Task.CompletedTask;
    }

    private ClusterPeerAddressResolver CreateResolver() =>
        new(
            _host.Services.GetRequiredService<IClusterNodeRepository>(),
            NullLogger<ClusterPeerAddressResolver>.Instance
        );

    private async Task SeedPeerAsync(string host, ClusterNodeStatusEnum status)
    {
        await _clusterNodeRepository.AddAsync(
            new ClusterNodeEntity
            {
                Id = Guid.NewGuid(),
                Name = $"peer-{host}",
                RemoteNodeId = Guid.NewGuid(),
                Host = host,
                Port = 443,
                TsigKeyName = "cluster-key",
                TlsCertificateThumbprint = "deadbeef",
                Status = status,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await _unitOfWork.SaveChangesAsync();
    }

    [Fact]
    public async Task ReturnsAnEmptyListWhenNoPeersAreRegisteredAtAll()
    {
        IReadOnlyList<string> addresses = await CreateResolver().ResolveHealthyPeerAddressesAsync();

        Assert.Empty(addresses);
    }

    [Fact]
    public async Task ResolvesAnIpLiteralHostDirectly()
    {
        await SeedPeerAsync("203.0.113.7", ClusterNodeStatusEnum.Healthy);

        IReadOnlyList<string> addresses = await CreateResolver().ResolveHealthyPeerAddressesAsync();

        Assert.Equal(["203.0.113.7"], addresses);
    }

    [Theory]
    [InlineData(ClusterNodeStatusEnum.Joining)]
    [InlineData(ClusterNodeStatusEnum.Unreachable)]
    public async Task ExcludesAPeerThatIsNotCurrentlyHealthy(ClusterNodeStatusEnum status)
    {
        await SeedPeerAsync("203.0.113.8", status);

        IReadOnlyList<string> addresses = await CreateResolver().ResolveHealthyPeerAddressesAsync();

        Assert.Empty(addresses);
    }

    [Fact]
    public async Task ReturnsEveryHealthyPeerAndSkipsUnhealthyOnesInTheSameCall()
    {
        await SeedPeerAsync("203.0.113.10", ClusterNodeStatusEnum.Healthy);
        await SeedPeerAsync("203.0.113.11", ClusterNodeStatusEnum.Unreachable);
        await SeedPeerAsync("203.0.113.12", ClusterNodeStatusEnum.Healthy);

        IReadOnlyList<string> addresses = await CreateResolver().ResolveHealthyPeerAddressesAsync();

        Assert.Equal(2, addresses.Count);
        Assert.Contains("203.0.113.10", addresses);
        Assert.Contains("203.0.113.12", addresses);
        Assert.DoesNotContain("203.0.113.11", addresses);
    }
}
