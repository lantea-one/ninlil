using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Cluster.Backfill;
using Ninlil.Dns.Server.Cluster.Tests.Backfill;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Tests.Backfill;

/// <summary>
///     This class tests <see cref="ClusterConfigBackfillRunner" /> against a temporary SQLite
///     database, fake sources and a recording sender with no network: when a pass removes or
///     keeps its request, the order of the stages, the cap on deliveries in flight, one pass at a
///     time per peer, passes to two peers at once, and the orphan rule (a request whose peer row
///     is gone is deleted without sending). The same orphan rule runs against MongoDB in
///     <c>Ninlil.Dns.Data.Integration.Tests</c>.
/// </summary>
public sealed class ClusterConfigBackfillRunnerTests
{
    [Fact]
    public async Task Pass_WhenEveryDeliverySucceeds_RemovesTheRequest()
    {
        using ClusterBackfillTestHost host = new(new ClusterBackfillTestHost.FakeSource(0, 3));
        Guid peerId = await host.AddPeerAsync();
        await host.AddRequestAsync(peerId);
        ClusterBackfillTestHost.RecordingSender sender = new();

        bool done = await host.CreateRunner(sender).RunPassAsync(peerId, CancellationToken.None);

        Assert.True(done);
        Assert.Equal(3, sender.Sent.Count);
        Assert.Null(await host.FindRequestAsync(peerId));
    }

    [Fact]
    public async Task Pass_WhenADeliveryFails_KeepsTheRequestAndRecordsTheError()
    {
        using ClusterBackfillTestHost host = new(new ClusterBackfillTestHost.FakeSource(0, 10));
        Guid peerId = await host.AddPeerAsync();
        await host.AddRequestAsync(peerId);
        ClusterBackfillTestHost.RecordingSender sender = new()
        {
            OnSend = (_, envelope) => Task.FromResult(envelope.EntityKey != "5"),
        };

        bool done = await host.CreateRunner(sender).RunPassAsync(peerId, CancellationToken.None);

        Assert.False(done);
        ClusterPeerConfigBackfillEntity request = await host.FindRequestAsync(peerId);
        Assert.NotNull(request);
        Assert.Equal(1, request.AttemptCount);
        Assert.NotNull(request.LastAttemptAtUtc);
        Assert.Contains("Stage0", request.LastError);
    }

    [Fact]
    public async Task Pass_WhenADeliveryThrows_KeepsTheRequestAndRecordsTheMessage()
    {
        using ClusterBackfillTestHost host = new(new ClusterBackfillTestHost.FakeSource(0, 3));
        Guid peerId = await host.AddPeerAsync();
        await host.AddRequestAsync(peerId);
        ClusterBackfillTestHost.RecordingSender sender = new()
        {
            OnSend = (_, _) => throw new HttpRequestException("connection refused"),
        };

        Assert.False(await host.CreateRunner(sender).RunPassAsync(peerId, CancellationToken.None));

        Assert.Contains("connection refused", (await host.FindRequestAsync(peerId)).LastError);
    }

    [Fact]
    public async Task Pass_WhenARequestArrivesDuringThePass_KeepsTheRequest()
    {
        using ClusterBackfillTestHost host = new(new ClusterBackfillTestHost.FakeSource(0, 3));
        Guid peerId = await host.AddPeerAsync();
        await host.AddRequestAsync(peerId);
        ClusterConfigBackfillRunner runner = null;
        ClusterBackfillTestHost.RecordingSender sender = new();
        runner = host.CreateRunner(sender);
        int first = 0;
        sender.OnSend = async (_, _) =>
        {
            // A second request (a missed live delivery) arrives while the first pass runs.
            if (Interlocked.Exchange(ref first, 1) == 0)
                await runner.RequestBackfillAsync(peerId);
            return true;
        };

        Assert.True(await runner.RunPassAsync(peerId, CancellationToken.None));

        // The pass delivered everything, but the newer request needs another pass.
        Assert.NotNull(await host.FindRequestAsync(peerId));
        Assert.True(await runner.RunPassAsync(peerId, CancellationToken.None));
        Assert.Null(await host.FindRequestAsync(peerId));
    }

    [Fact]
    public async Task Pass_SendsEachStageBeforeTheNext()
    {
        // Registered out of order on purpose: the runner orders by stage, not by registration.
        using ClusterBackfillTestHost host = new(
            new ClusterBackfillTestHost.FakeSource(2, 6),
            new ClusterBackfillTestHost.FakeSource(0, 6),
            new ClusterBackfillTestHost.FakeSource(3, 6),
            new ClusterBackfillTestHost.FakeSource(1, 6)
        );
        Guid peerId = await host.AddPeerAsync();
        await host.AddRequestAsync(peerId);
        ClusterBackfillTestHost.RecordingSender sender = new()
        {
            Delay = TimeSpan.FromMilliseconds(5),
        };

        Assert.True(await host.CreateRunner(sender).RunPassAsync(peerId, CancellationToken.None));

        int[] stages = [.. sender.Sent.Select(sent => int.Parse(sent.Envelope.EntityType[5..]))];
        Assert.Equal(24, stages.Length);
        Assert.Equal(stages.Order(), stages);
    }

    [Fact]
    public async Task Pass_NeverHasMoreThanFourDeliveriesInFlight()
    {
        using ClusterBackfillTestHost host = new(new ClusterBackfillTestHost.FakeSource(0, 40));
        Guid peerId = await host.AddPeerAsync();
        await host.AddRequestAsync(peerId);
        ClusterBackfillTestHost.RecordingSender sender = new()
        {
            Delay = TimeSpan.FromMilliseconds(10),
        };

        Assert.True(await host.CreateRunner(sender).RunPassAsync(peerId, CancellationToken.None));

        // The delay makes the cap observable: it is reached and never passed.
        Assert.Equal(ClusterConfigBackfillRunner.MaxDeliveriesInFlight, sender.MaxInFlight);
    }

    [Fact]
    public async Task Pass_ToTwoPeersAtOnce_SendsEachTheFullSet()
    {
        using ClusterBackfillTestHost host = new(
            new ClusterBackfillTestHost.FakeSource(0, 8),
            new ClusterBackfillTestHost.FakeSource(1, 8)
        );
        Guid first = await host.AddPeerAsync();
        Guid second = await host.AddPeerAsync();
        await host.AddRequestAsync(first);
        await host.AddRequestAsync(second);
        ClusterBackfillTestHost.RecordingSender sender = new()
        {
            Delay = TimeSpan.FromMilliseconds(2),
        };
        ClusterConfigBackfillRunner runner = host.CreateRunner(sender);

        bool[] done = await Task.WhenAll(
            runner.RunPassAsync(first, CancellationToken.None),
            runner.RunPassAsync(second, CancellationToken.None)
        );

        Assert.All(done, Assert.True);
        Assert.Equal(16, sender.Sent.Count(sent => sent.PeerId == first));
        Assert.Equal(16, sender.Sent.Count(sent => sent.PeerId == second));
        Assert.Null(await host.FindRequestAsync(first));
        Assert.Null(await host.FindRequestAsync(second));
    }

    [Fact]
    public async Task TryStart_WhileAPassToThatPeerRuns_ReturnsFalse()
    {
        using ClusterBackfillTestHost host = new(new ClusterBackfillTestHost.FakeSource(0, 2));
        Guid peerId = await host.AddPeerAsync();
        await host.AddRequestAsync(peerId);
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ClusterBackfillTestHost.RecordingSender sender = new()
        {
            OnSend = async (_, _) =>
            {
                started.TrySetResult();
                await release.Task;
                return true;
            },
        };
        await using ClusterConfigBackfillRunner runner = host.CreateRunner(sender);

        Assert.True(runner.TryStart(peerId));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(runner.TryStart(peerId));

        release.SetResult();
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (await host.FindRequestAsync(peerId) is not null && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.Null(await host.FindRequestAsync(peerId));

        // Once the pass has ended a new one can start.
        deadline = DateTime.UtcNow.AddSeconds(10);
        while (!runner.TryStart(peerId) && DateTime.UtcNow < deadline)
            await Task.Delay(20);
    }

    [Fact]
    public async Task StopAsync_WithAPassInFlight_CancelsAndAwaitsItKeepsTheRequestAndRefusesNewPasses()
    {
        using ClusterBackfillTestHost host = new(new ClusterBackfillTestHost.FakeSource(0, 4));
        Guid peerId = await host.AddPeerAsync();
        await host.AddRequestAsync(peerId);
        ClusterBackfillTestHost.RecordingSender sender = new() { BlockUntilCancelled = true };
        ClusterConfigBackfillRunner runner = host.CreateRunner(sender);
        Assert.True(runner.TryStart(peerId));
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (sender.Sent.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.NotEmpty(sender.Sent);

        await runner.StopAsync(CancellationToken.None);

        // The blocked delivery was cancelled and has ended by the time StopAsync returns.
        Assert.Equal(sender.Sent.Count, sender.Finished);
        ClusterPeerConfigBackfillEntity request = await host.FindRequestAsync(peerId);
        Assert.NotNull(request);
        Assert.Equal(0, request.AttemptCount);
        Assert.False(runner.TryStart(peerId));
        await runner.DisposeAsync();
        // The container disposes an instance registered under several types more than once.
        await runner.DisposeAsync();
        Assert.False(runner.TryStart(peerId));
    }

    [Fact]
    public async Task StopAsync_AfterDisposeAsync_DoesNothingAndRefusesNewPasses()
    {
        using ClusterBackfillTestHost host = new(new ClusterBackfillTestHost.FakeSource(0, 1));
        Guid peerId = await host.AddPeerAsync();
        ClusterConfigBackfillRunner runner = host.CreateRunner(
            new ClusterBackfillTestHost.RecordingSender()
        );
        await runner.DisposeAsync();

        // The host stops its services from more than one thread while the container disposes
        // them, so a stop can arrive after the dispose.
        await runner.StopAsync(CancellationToken.None);
        await runner.StopAsync(CancellationToken.None);

        Assert.False(runner.TryStart(peerId));
    }

    [Fact]
    public async Task StopAsync_RunningWhileDisposeAsyncRuns_NeverThrows()
    {
        using ClusterBackfillTestHost host = new(new ClusterBackfillTestHost.FakeSource(0, 1));
        for (int i = 0; i < 10; i++)
        {
            ClusterConfigBackfillRunner runner = host.CreateRunner(
                new ClusterBackfillTestHost.RecordingSender()
            );
            Task stop = Task.Run(() => runner.StopAsync(CancellationToken.None));
            Task dispose = Task.Run(() => runner.DisposeAsync().AsTask());
            Task lateStop = Task.Run(() => runner.StopAsync(CancellationToken.None));

            await Task.WhenAll(stop, dispose, lateStop);
        }
    }

    [Fact]
    public async Task Pass_ForARequestWhosePeerRowIsGone_DeletesTheRequestAndSendsNothing()
    {
        using ClusterBackfillTestHost host = new(new ClusterBackfillTestHost.FakeSource(0, 3));
        Guid peerId = await host.AddPeerAsync();
        await host.AddRequestAsync(peerId);
        await host.DeletePeerKeepingRequestAsync(peerId);
        Assert.NotNull(await host.FindRequestAsync(peerId));
        ClusterBackfillTestHost.RecordingSender sender = new();

        bool done = await host.CreateRunner(sender).RunPassAsync(peerId, CancellationToken.None);

        Assert.True(done);
        Assert.Empty(sender.Sent);
        Assert.Null(await host.FindRequestAsync(peerId));
    }

    [Fact]
    public async Task RequestBackfillAsync_ForAJoiningPeer_RecordsNothing()
    {
        using ClusterBackfillTestHost host = new();
        Guid joining = await host.AddPeerAsync(ClusterNodeStatusEnum.Joining);
        Guid healthy = await host.AddPeerAsync();
        ClusterConfigBackfillRunner runner = host.CreateRunner(
            new ClusterBackfillTestHost.RecordingSender()
        );

        Assert.False(await runner.RequestBackfillAsync(joining));
        Assert.False(await runner.RequestBackfillAsync(Guid.NewGuid()));
        Assert.True(await runner.RequestBackfillAsync(healthy));
        Assert.True(await runner.RequestBackfillAsync(healthy));

        Assert.Null(await host.FindRequestAsync(joining));
        Assert.NotNull(await host.FindRequestAsync(healthy));
    }
}
