using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Cluster.Backfill;
using Ninlil.Dns.Server.Cluster.Channel;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Tests.Backfill;

/// <summary>
///     This class tests what <see cref="ClusterConfigSyncBroadcaster" /> records for a peer that
///     misses a live broadcast: a backfill request for an unreachable peer and for a failed
///     delivery, none for a joining peer or a delivery that succeeds, and one row however many
///     deliveries fail. The broadcaster runs over a real SQLite database and a stub HTTP client
///     (the <c>CreateClient</c> test seam), so no peer listens.
/// </summary>
public sealed class ClusterConfigSyncBroadcasterBackfillTests : IDisposable
{
    private readonly ClusterBackfillTestHost _host = new();

    /// <inheritdoc />
    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task BroadcastAsync_ForAnUnreachablePeer_RecordsABackfillRequest()
    {
        Guid peerId = await _host.AddPeerAsync(ClusterNodeStatusEnum.Unreachable);

        await CreateBroadcaster(HttpStatusCode.OK).BroadcastAsync(NewEnvelope(), default);

        Assert.NotNull(await _host.FindRequestAsync(peerId));
    }

    [Fact]
    public async Task BroadcastAsync_WhenADeliveryFails_RecordsABackfillRequestForThatPeer()
    {
        Guid peerId = await _host.AddPeerAsync();

        await CreateBroadcaster(HttpStatusCode.InternalServerError)
            .BroadcastAsync(NewEnvelope(), default);

        Assert.NotNull(await _host.FindRequestAsync(peerId));
    }

    [Fact]
    public async Task BroadcastAsync_WhenAPeerCannotBeReached_RecordsABackfillRequestForThatPeer()
    {
        Guid peerId = await _host.AddPeerAsync();

        await CreateBroadcaster(HttpStatusCode.OK, throwConnectionFailure: true)
            .BroadcastAsync(NewEnvelope(), default);

        Assert.NotNull(await _host.FindRequestAsync(peerId));
    }

    [Fact]
    public async Task BroadcastAsync_ForAJoiningPeer_RecordsNoRequest()
    {
        Guid peerId = await _host.AddPeerAsync(ClusterNodeStatusEnum.Joining);

        await CreateBroadcaster(HttpStatusCode.OK).BroadcastAsync(NewEnvelope(), default);

        Assert.Null(await _host.FindRequestAsync(peerId));
    }

    [Fact]
    public async Task BroadcastAsync_WhenEveryDeliverySucceeds_RecordsNoRequest()
    {
        Guid healthy = await _host.AddPeerAsync();

        await CreateBroadcaster(HttpStatusCode.OK).BroadcastAsync(NewEnvelope(), default);

        Assert.Null(await _host.FindRequestAsync(healthy));
    }

    [Fact]
    public async Task BroadcastAsync_ForAFailingAndAnUnreachablePeer_RecordsARequestForEach()
    {
        // One broadcast reaches a failing peer and skips an unreachable one.
        Guid unreachable = await _host.AddPeerAsync(ClusterNodeStatusEnum.Unreachable);
        Guid failing = await _host.AddPeerAsync();
        ClusterConfigSyncBroadcaster broadcaster = CreateBroadcaster(
            HttpStatusCode.InternalServerError
        );

        await broadcaster.BroadcastAsync(NewEnvelope(), default);

        Assert.NotNull(await _host.FindRequestAsync(unreachable));
        Assert.NotNull(await _host.FindRequestAsync(failing));
    }

    [Fact]
    public async Task BroadcastAsync_RepeatedFailuresToOnePeer_LeaveOneRequestRow()
    {
        Guid peerId = await _host.AddPeerAsync(ClusterNodeStatusEnum.Unreachable);
        ClusterConfigSyncBroadcaster broadcaster = CreateBroadcaster(HttpStatusCode.OK);

        await Task.WhenAll(
            Enumerable.Range(0, 20).Select(_ => broadcaster.BroadcastAsync(NewEnvelope(), default))
        );
        await broadcaster.BroadcastAsync(NewEnvelope(), default);

        Assert.Single(await _host.ListPendingAsync(), peerId);
    }

    [Fact]
    public async Task BroadcastAsync_AfterAPassStartedReadingTheRequest_RecordsAgain()
    {
        Guid peerId = await _host.AddPeerAsync(ClusterNodeStatusEnum.Unreachable);
        ClusterConfigBackfillRequester requester = _host.CreateRequester();
        ClusterConfigSyncBroadcaster broadcaster = CreateBroadcaster(
            HttpStatusCode.OK,
            requester: requester
        );
        await broadcaster.BroadcastAsync(NewEnvelope(), default);
        DateTimeOffset first = (await _host.FindRequestAsync(peerId)).RequestedAtUtc;

        // A repeat before any pass reads the row is answered from memory: the time stays.
        await Task.Delay(20);
        await broadcaster.BroadcastAsync(NewEnvelope(), default);
        Assert.Equal(first, (await _host.FindRequestAsync(peerId)).RequestedAtUtc);

        // Once a pass has started, a new miss must move the time so the pass keeps the row.
        requester.NotePassStarted(peerId);
        await Task.Delay(20);
        await broadcaster.BroadcastAsync(NewEnvelope(), default);
        Assert.True((await _host.FindRequestAsync(peerId)).RequestedAtUtc > first);
    }

    /// <summary>This method builds a broadcaster whose peers all answer with <paramref name="status" />.</summary>
    private ClusterConfigSyncBroadcaster CreateBroadcaster(
        HttpStatusCode status,
        bool throwConnectionFailure = false,
        ClusterConfigBackfillRequester requester = null
    ) =>
        new(
            _host.Services.GetRequiredService<IServiceScopeFactory>(),
            new ClusterBackfillTestHost.StubClientFactory(
                _host.Services.GetRequiredService<IServiceScopeFactory>(),
                _host.Services.GetRequiredService<TsigSecretProtector>(),
                status,
                throwConnectionFailure
            ),
            new ClusterBackfillTestHost.FixedOptionsMonitor<ClusterOptions>(
                new ClusterOptions { Enable = true }
            ),
            requester ?? _host.CreateRequester(),
            NullLogger<ClusterConfigSyncBroadcaster>.Instance
        );

    private static ClusterConfigSyncEnvelope NewEnvelope() =>
        new("Stage1", Guid.NewGuid().ToString(), false, "{}", DateTimeOffset.UtcNow, Guid.Empty);
}
