using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Channel;
using Ninlil.Dns.Server.Cluster.Invite;
using Ninlil.Dns.Server.Cluster.Join;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Cluster.Tests.Backfill;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Tests;

/// <summary>
///     This class tests which node records a config backfill request when a join completes: the
///     inviting node records one for the new peer, in the same save as the peer row and only once,
///     and the joining node records none for the inviter. It uses <see cref="ClusterJoinService" />
///     with real repositories on a temporary SQLite database and a stub client for the callback.
/// </summary>
public sealed class ClusterJoinServiceBackfillTests : IDisposable
{
    private readonly ClusterBackfillTestHost _host = new();

    private readonly string _certificateStatePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-cluster-join-backfill-tests-{Guid.NewGuid():N}.pfx"
    );

    private readonly ReloadableTlsCertificateProvider _certificateProvider;

    private readonly ClusterOptions _options = new()
    {
        Enable = true,
        NodeId = Guid.NewGuid(),
        AdvertisedHost = "node.example",
    };

    public ClusterJoinServiceBackfillTests() =>
        _certificateProvider = new ReloadableTlsCertificateProvider(
            new TlsCertificateOptions { SelfSignedCertificateStatePath = _certificateStatePath },
            NullLogger.Instance
        );

    /// <inheritdoc />
    public void Dispose()
    {
        _host.Dispose();
        _certificateProvider.Dispose();
        TemporaryFiles.DeleteFile(_certificateStatePath);
    }

    [Fact]
    public async Task HandleJoinCompleteAsync_ForANewPeer_RecordsABackfillRequest()
    {
        string keyName = await AddInviteKeyAsync();
        Guid joinerNodeId = Guid.NewGuid();

        ClusterNodeEntity peer = await HandleAsync(keyName, joinerNodeId);

        ClusterPeerConfigBackfillEntity request = await _host.FindRequestAsync(peer.Id);
        Assert.NotNull(request);
        Assert.Equal(peer.Id, request.ClusterNodeId);
    }

    [Fact]
    public async Task HandleJoinCompleteAsync_ForARepeatedCallback_AddsNoSecondRequest()
    {
        string keyName = await AddInviteKeyAsync();
        Guid joinerNodeId = Guid.NewGuid();
        ClusterNodeEntity peer = await HandleAsync(keyName, joinerNodeId);
        ClusterPeerConfigBackfillEntity first = await _host.FindRequestAsync(peer.Id);

        await HandleAsync(keyName, joinerNodeId);

        using IServiceScope scope = _host.Services.CreateScope();
        IReadOnlyList<ClusterPeerConfigBackfillEntity> all = await scope
            .ServiceProvider.GetRequiredService<IClusterPeerConfigBackfillRepository>()
            .ListAsync();
        ClusterPeerConfigBackfillEntity only = Assert.Single(all);
        Assert.Equal(first.Id, only.Id);
    }

    [Fact]
    public async Task JoinAsync_OnTheJoiningNode_RecordsNoRequestForTheInviter()
    {
        ClusterInviteCode invite = new()
        {
            InviterNodeId = Guid.NewGuid(),
            InviterHost = "inviter.example",
            InviterPort = 443,
            TsigKeyName = $"cluster-invite-{Guid.NewGuid():N}",
            TsigKeySecretBase64 = Convert.ToBase64String("a-shared-cluster-channel-secret!"u8),
            InviterCertificateThumbprint = new string('b', 64),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10),
        };

        ClusterNodeEntity inviter;
        using (IServiceScope scope = _host.Services.CreateScope())
        {
            inviter = await CreateService(scope, HttpStatusCode.OK)
                .JoinAsync(ClusterInviteCodec.Encode(invite), "inviter", CancellationToken.None);
        }

        Assert.Equal(ClusterNodeStatusEnum.Joining, inviter.Status);
        Assert.Null(await _host.FindRequestAsync(inviter.Id));
        using IServiceScope check = _host.Services.CreateScope();
        Assert.Empty(
            await check
                .ServiceProvider.GetRequiredService<IClusterPeerConfigBackfillRepository>()
                .ListAsync()
        );
    }

    private async Task<ClusterNodeEntity> HandleAsync(string keyName, Guid joinerNodeId)
    {
        using IServiceScope scope = _host.Services.CreateScope();
        return await CreateService(scope, HttpStatusCode.OK)
            .HandleJoinCompleteAsync(
                keyName,
                new ClusterJoinCompleteRequest(
                    joinerNodeId,
                    "joiner.example",
                    443,
                    new string('c', 64),
                    "joiner"
                ),
                CancellationToken.None
            );
    }

    private ClusterJoinService CreateService(IServiceScope scope, HttpStatusCode callbackStatus)
    {
        IServiceProvider provider = scope.ServiceProvider;
        return new ClusterJoinService(
            provider.GetRequiredService<IClusterNodeRepository>(),
            provider.GetRequiredService<IClusterPeerConfigBackfillRepository>(),
            provider.GetRequiredService<ITsigKeyRepository>(),
            provider.GetRequiredService<IUnitOfWork>(),
            provider.GetRequiredService<TsigSecretProtector>(),
            new ClusterBackfillTestHost.StubClientFactory(
                _host.Services.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<TsigSecretProtector>(),
                callbackStatus
            ),
            new ClusterBackfillTestHost.FixedOptionsMonitor<ClusterOptions>(_options),
            _certificateProvider
        );
    }

    /// <summary>This method saves an unused, unexpired invite key and returns its name.</summary>
    private async Task<string> AddInviteKeyAsync()
    {
        using IServiceScope scope = _host.Services.CreateScope();
        string keyName = $"cluster-invite-{Guid.NewGuid():N}";
        await scope
            .ServiceProvider.GetRequiredService<ITsigKeyRepository>()
            .AddAsync(
                new TsigKeyEntity
                {
                    Id = Guid.NewGuid(),
                    Name = keyName,
                    AlgorithmName = "hmac-sha256",
                    SecretEncrypted = "unused",
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ClusterInviteExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(10),
                }
            );
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return keyName;
    }
}
