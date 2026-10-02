using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Server.Cluster.Heartbeat;
using Ninlil.Dns.Server.Cluster.Http;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Tests;

/// <summary>
///     This class tests <see cref="ClusterHeartbeatBackgroundService" /> against EF Core
///     repositories on a temporary SQLite database, with a peer that is a loopback TCP listener
///     which accepts connections and never answers, so every probe runs into the HTTP timeout.
///     The service runs with a short interval and timeout, and the test waits for the peer's
///     stored status to change.
/// </summary>
public sealed class ClusterHeartbeatBackgroundServiceTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-cluster-heartbeat-tests-{Guid.NewGuid():N}.sqlite3"
    );

    private readonly ServiceProvider _services;

    public ClusterHeartbeatBackgroundServiceTests()
    {
        ServiceCollection services = new();
        services.AddDbContext<NinlilDnsContext>(builder =>
            builder.UseNinlilDnsSqlite($"Data Source={_databasePath}")
        );
        services.AddScoped<IUnitOfWork, EntityFrameworkUnitOfWork>();
        services.AddScoped<IClusterNodeRepository, ClusterNodeRepository>();
        services.AddScoped<ITsigKeyRepository, TsigKeyRepository>();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddSingleton<TsigSecretProtector>();
        _services = services.BuildServiceProvider();

        using IServiceScope scope = _services.CreateScope();
        scope.ServiceProvider.GetRequiredService<NinlilDnsContext>().Database.EnsureCreated();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _services.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    /// <summary>
    ///     This test checks that a probe timeout counts as a missed heartbeat. A timeout surfaces
    ///     as a <see cref="TaskCanceledException" />, which must not abort the round, or the
    ///     peer would never be marked unreachable.
    /// </summary>
    [Fact]
    public async Task APeerThatTimesOutIsMarkedUnreachable()
    {
        using TcpListener silentPeer = new(IPAddress.Loopback, 0);
        silentPeer.Start();
        int port = ((IPEndPoint)silentPeer.LocalEndpoint).Port;

        Guid peerRowId = await SeedPeerAsync(port);

        ClusterOptions options = new()
        {
            Enable = true,
            NodeId = Guid.NewGuid(),
            HeartbeatInterval = TimeSpan.FromMilliseconds(100),
            HeartbeatTimeout = TimeSpan.FromMilliseconds(300),
            MissedHeartbeatThreshold = 1,
        };

        ClusterHeartbeatBackgroundService service = new(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new ClusterPeerHttpClientFactory(
                _services.GetRequiredService<IServiceScopeFactory>(),
                _services.GetRequiredService<TsigSecretProtector>()
            ),
            new FixedOptionsMonitor<ClusterOptions>(options),
            NullLogger<ClusterHeartbeatBackgroundService>.Instance
        );

        await service.StartAsync(CancellationToken.None);
        try
        {
            ClusterNodeStatusEnum status = ClusterNodeStatusEnum.Healthy;
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);

            while (status != ClusterNodeStatusEnum.Unreachable && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);

                using IServiceScope scope = _services.CreateScope();
                ClusterNodeEntity peer = await scope
                    .ServiceProvider.GetRequiredService<IClusterNodeRepository>()
                    .GetByIdAsync(peerRowId);
                status = peer.Status;
            }

            Assert.Equal(ClusterNodeStatusEnum.Unreachable, status);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    ///     This method saves a healthy peer on the given loopback port, and the key the probe is
    ///     signed with.
    /// </summary>
    private async Task<Guid> SeedPeerAsync(int port)
    {
        using IServiceScope scope = _services.CreateScope();
        IServiceProvider provider = scope.ServiceProvider;

        string keyName = $"cluster-{Guid.NewGuid():N}";
        await provider
            .GetRequiredService<ITsigKeyRepository>()
            .AddAsync(
                new TsigKeyEntity
                {
                    Id = Guid.NewGuid(),
                    Name = keyName,
                    AlgorithmName = "hmac-sha256",
                    SecretEncrypted = provider
                        .GetRequiredService<TsigSecretProtector>()
                        .Protect("a-shared-cluster-channel-secret!"u8.ToArray()),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );

        ClusterNodeEntity peer = new()
        {
            Id = Guid.NewGuid(),
            Name = "silent-peer",
            RemoteNodeId = Guid.NewGuid(),
            Host = "127.0.0.1",
            Port = port,
            TsigKeyName = keyName,
            TlsCertificateThumbprint = new string('a', 64),
            Status = ClusterNodeStatusEnum.Healthy,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        await provider.GetRequiredService<IClusterNodeRepository>().AddAsync(peer);
        await provider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        return peer.Id;
    }

    /// <summary>
    ///     This class is an <see cref="IOptionsMonitor{TOptions}" /> that always returns one
    ///     instance and never reports a change.
    /// </summary>
    private sealed class FixedOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue => value;

        public TOptions Get(string name) => value;

        public IDisposable OnChange(Action<TOptions, string> listener) => null;
    }
}
