using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests;

/// <summary>
///     This class proves, against a real, running <see cref="DnsQueryProcessingPipeline" /> and a
///     real reload-on-change JSON configuration file -- not merely
///     <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> read in isolation -- the split hot-
///     reload behavior this class's own doc comment describes: a write reaching
///     <see cref="DnsQueryProcessingPipelineOptions.MaxUdpAmplificationFactor" /> via
///     <see cref="Server.Presets.PresetConfigurationFileApplier" /> (the exact mechanism
///     <c>Ninlil.Web.Controllers.DnsPipelineSettingsController.Patch</c> itself calls)
///     changes the very next query's own answer with no restart, while the identical write
///     mechanism reaching <see cref="DnsQueryProcessingPipelineOptions.ChannelCapacity" /> changes
///     nothing about an already-running pipeline until it is genuinely restarted
///     (<see cref="DnsQueryProcessingPipeline.StopAsync" /> then
///     <see cref="DnsQueryProcessingPipeline.StartAsync" /> on the same instance, exactly what a
///     <c>POST /api/services/query-processing-pipeline/restart</c> call does).
/// </summary>
public sealed class DnsQueryProcessingPipelineHotReloadTests
{
    private static readonly IPAddress RemoteAddress = IPAddress.Parse("203.0.113.77");

    private sealed class AlwaysAnswerResolver(Func<DnsQueryContext, DnsMessage> buildAnswer)
        : IDnsQueryResolver
    {
        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(DnsQueryResolution.Handled(buildAnswer(context)));
    }

    /// <summary>
    ///     This resolver never completes on its own -- every call blocks until the test itself
    ///     releases <see cref="Gate" /> once per call -- letting a test deterministically control
    ///     exactly how many queries a worker has "in flight" at once, rather than racing against a
    ///     real timing window.
    /// </summary>
    private sealed class GatedResolver : IDnsQueryResolver
    {
        public readonly SemaphoreSlim Gate = new(0);
        public int CallCount;

        public async ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            Interlocked.Increment(ref CallCount);
            await Gate.WaitAsync(stoppingToken);

            return DnsQueryResolution.Handled(
                new DnsMessage
                {
                    Id = context.Request.Id,
                    IsResponse = true,
                    Opcode = context.Request.Opcode,
                    ResponseCode = DnsResponseCodeEnum.NoError,
                }
            );
        }
    }

    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }

    private static DnsMessage BuildQuery(bool withEdns = false) =>
        new()
        {
            Id = 1,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("example.com."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
            // A no-EDNS query is always capped at RFC 1035's own flat 512-byte ceiling
            // regardless of MaxUdpAmplificationFactor (the ceiling this pipeline computes is the
            // smaller of the two), so proving the amplification factor itself hot-reloads needs
            // a query that declares a larger EDNS payload size instead -- otherwise the factor
            // can never become the binding constraint at all.
            Edns = withEdns ? new DnsEdnsOptions(0, 4096, false, []) : null,
        };

    private static DnsMessage BuildAnswerWithManyRecords(DnsQueryContext context, int recordCount)
    {
        DnsMessage response = new()
        {
            Id = context.Request.Id,
            IsResponse = true,
            Opcode = context.Request.Opcode,
            ResponseCode = DnsResponseCodeEnum.NoError,
        };

        for (int i = 0; i < recordCount; i++)
            response.Answers.Add(
                new DnsResourceRecord(
                    DnsName.Parse("example.com."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    300,
                    new byte[] { 192, 0, 2, (byte)(i % 256) }
                )
            );

        return response;
    }

    private static ValueTask Capture(
        TaskCompletionSource<byte[]> tcs,
        ReadOnlyMemory<byte> bytes,
        CancellationToken _
    )
    {
        tcs.TrySetResult(bytes.ToArray());
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_ChangesTheAmplificationCapTheVeryNextQueryUses_WithNoRestart()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-pipeline-amplification-hot-reload-{Guid.NewGuid():N}.json"
        );

        DnsQueryProcessingPipeline pipeline = null;

        try
        {
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile(path, optional: true, reloadOnChange: true)
                .Build();

            ServiceCollection services = new();
            services.Configure<DnsQueryProcessingPipelineOptions>(
                configuration.GetSection(DnsQueryProcessingPipelineOptions.SectionName)
            );
            await using ServiceProvider provider = services.BuildServiceProvider();

            IOptionsMonitor<DnsQueryProcessingPipelineOptions> monitor =
                provider.GetRequiredService<IOptionsMonitor<DnsQueryProcessingPipelineOptions>>();

            // A tight amplification cap: a 50-record answer to a bare, no-EDNS query will be
            // truncated.
            await Server.Presets.PresetConfigurationFileApplier.ApplyAsync(
                [
                    new Server.Presets.PresetConfigurationSetting(
                        DnsQueryProcessingPipelineOptions.SectionName,
                        nameof(DnsQueryProcessingPipelineOptions.MaxUdpAmplificationFactor),
                        1.0
                    ),
                ],
                path
            );

            await WaitForReloadAsync(
                monitor,
                () => monitor.CurrentValue.MaxUdpAmplificationFactor == 1.0
            );

            AlwaysAnswerResolver resolver = new(context => BuildAnswerWithManyRecords(context, 50));
            DnsQueryDispatcher dispatcher = new([resolver], new NoOpEventPublisher());
            pipeline = new DnsQueryProcessingPipeline(
                dispatcher,
                monitor,
                NullLogger<DnsQueryProcessingPipeline>.Instance
            );
            await pipeline.StartAsync(CancellationToken.None);

            TaskCompletionSource<byte[]> firstResponse = new();
            await pipeline.EnqueueAsync(
                new DnsInboundMessage(
                    DnsMessageWriter.Write(BuildQuery(withEdns: true)),
                    DnsTransportProtocolEnum.Udp,
                    RemoteAddress,
                    (bytes, token) => Capture(firstResponse, bytes, token)
                )
            );

            byte[] firstBytes = await firstResponse.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(DnsMessageReader.Read(firstBytes, out _).IsTruncated);

            // The exact same write mechanism DnsPipelineSettingsController.Patch itself calls,
            // this time loosening the cap enough that the same-shaped answer fits.
            await Server.Presets.PresetConfigurationFileApplier.ApplyAsync(
                [
                    new Server.Presets.PresetConfigurationSetting(
                        DnsQueryProcessingPipelineOptions.SectionName,
                        nameof(DnsQueryProcessingPipelineOptions.MaxUdpAmplificationFactor),
                        1000.0
                    ),
                ],
                path
            );

            // Polls the actual settled value directly rather than trusting a single OnChange
            // firing to mean the reload is fully applied -- a second write to the same monitor
            // within one test can otherwise race a stray/delayed notification left over from the
            // first write against this one, observed directly as a real, reproducible flake.
            await WaitForReloadAsync(
                monitor,
                () => monitor.CurrentValue.MaxUdpAmplificationFactor == 1000.0
            );

            // The identical, still-running pipeline instance now genuinely stops truncating --
            // proof the cap was actually re-read on this next query rather than captured once at
            // StartAsync.
            TaskCompletionSource<byte[]> secondResponse = new();
            await pipeline.EnqueueAsync(
                new DnsInboundMessage(
                    DnsMessageWriter.Write(BuildQuery(withEdns: true)),
                    DnsTransportProtocolEnum.Udp,
                    RemoteAddress,
                    (bytes, token) => Capture(secondResponse, bytes, token)
                )
            );

            byte[] secondBytes = await secondResponse.Task.WaitAsync(TimeSpan.FromSeconds(5));
            DnsMessage secondMessage = DnsMessageReader.Read(secondBytes, out _);
            Assert.False(secondMessage.IsTruncated);
            Assert.Equal(50, secondMessage.Answers.Count);
        }
        finally
        {
            if (pipeline is not null)
            {
                await pipeline.StopAsync(CancellationToken.None);
                await pipeline.DisposeAsync();
            }

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Best-effort cleanup only -- a lingering temp file from a test run is harmless.
            }
        }
    }

    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_OnlyChangesChannelCapacityAfterARestart_NeverLive()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-pipeline-channel-capacity-hot-reload-{Guid.NewGuid():N}.json"
        );

        DnsQueryProcessingPipeline pipeline = null;

        try
        {
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile(path, optional: true, reloadOnChange: true)
                .Build();

            ServiceCollection services = new();
            services.Configure<DnsQueryProcessingPipelineOptions>(
                configuration.GetSection(DnsQueryProcessingPipelineOptions.SectionName)
            );
            await using ServiceProvider provider = services.BuildServiceProvider();

            IOptionsMonitor<DnsQueryProcessingPipelineOptions> monitor =
                provider.GetRequiredService<IOptionsMonitor<DnsQueryProcessingPipelineOptions>>();

            await Server.Presets.PresetConfigurationFileApplier.ApplyAsync(
                [
                    new Server.Presets.PresetConfigurationSetting(
                        DnsQueryProcessingPipelineOptions.SectionName,
                        nameof(DnsQueryProcessingPipelineOptions.WorkerCount),
                        1
                    ),
                    new Server.Presets.PresetConfigurationSetting(
                        DnsQueryProcessingPipelineOptions.SectionName,
                        nameof(DnsQueryProcessingPipelineOptions.ChannelCapacity),
                        1
                    ),
                ],
                path
            );

            await WaitForReloadAsync(monitor, () => monitor.CurrentValue.ChannelCapacity == 1);

            GatedResolver resolver = new();
            DnsQueryDispatcher dispatcher = new([resolver], new NoOpEventPublisher());
            pipeline = new DnsQueryProcessingPipeline(
                dispatcher,
                monitor,
                NullLogger<DnsQueryProcessingPipeline>.Instance
            );
            await pipeline.StartAsync(CancellationToken.None);

            TaskCompletionSource<byte[]> responseA = new();
            await pipeline.EnqueueAsync(
                new DnsInboundMessage(
                    DnsMessageWriter.Write(BuildQuery()),
                    DnsTransportProtocolEnum.Udp,
                    RemoteAddress,
                    (bytes, token) => Capture(responseA, bytes, token)
                )
            );

            // Wait for the single worker to actually dequeue and start resolving query A (where
            // it now blocks on the gate), leaving the channel itself empty again.
            await WaitForConditionAsync(() => Volatile.Read(ref resolver.CallCount) >= 1);

            TaskCompletionSource<byte[]> responseB = new();
            await pipeline.EnqueueAsync(
                new DnsInboundMessage(
                    DnsMessageWriter.Write(BuildQuery()),
                    DnsTransportProtocolEnum.Udp,
                    RemoteAddress,
                    (bytes, token) => Capture(responseB, bytes, token)
                )
            );

            // The one-slot channel is now full (query B occupies it, the one worker is stuck on
            // query A) -- a third enqueue must block until space frees up.
            TaskCompletionSource<byte[]> responseC = new();
            ValueTask enqueueCValueTask = pipeline.EnqueueAsync(
                new DnsInboundMessage(
                    DnsMessageWriter.Write(BuildQuery()),
                    DnsTransportProtocolEnum.Udp,
                    RemoteAddress,
                    (bytes, token) => Capture(responseC, bytes, token)
                )
            );
            Task enqueueCTask = enqueueCValueTask.AsTask();

            await Task.Delay(200);
            Assert.False(
                enqueueCTask.IsCompleted,
                "Expected the bounded channel (capacity 1) to still be full."
            );

            // The exact same write mechanism DnsPipelineSettingsController.Patch itself calls --
            // raising ChannelCapacity while the pipeline keeps running, with no restart.
            await Server.Presets.PresetConfigurationFileApplier.ApplyAsync(
                [
                    new Server.Presets.PresetConfigurationSetting(
                        DnsQueryProcessingPipelineOptions.SectionName,
                        nameof(DnsQueryProcessingPipelineOptions.ChannelCapacity),
                        100
                    ),
                ],
                path
            );

            // Polls the actual settled value directly rather than trusting a single OnChange
            // firing to mean the reload is fully applied -- a second write to the same monitor
            // within one test can otherwise race a stray/delayed notification left over from the
            // first write against this one, observed directly as a real, reproducible flake.
            await WaitForReloadAsync(monitor, () => monitor.CurrentValue.ChannelCapacity == 100);

            // Even though the options binding itself now reports 100, the already-running
            // pipeline's own bounded Channel<T> was constructed with capacity 1 back at
            // StartAsync and is never rebuilt without a real restart -- the third enqueue must
            // still be blocked.
            await Task.Delay(200);
            Assert.False(
                enqueueCTask.IsCompleted,
                "ChannelCapacity must not hot-reload -- it is only re-read on the next StartAsync."
            );

            // Drain the pipeline: release query A, letting the worker dequeue query B, which
            // frees the slot query C's write was waiting on.
            resolver.Gate.Release();
            await responseA.Task.WaitAsync(TimeSpan.FromSeconds(5));
            resolver.Gate.Release();
            await responseB.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await enqueueCTask.WaitAsync(TimeSpan.FromSeconds(5));
            resolver.Gate.Release();
            await responseC.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Now genuinely restart the pipeline -- exactly what a
            // POST /api/services/query-processing-pipeline/restart call does -- and confirm the
            // new ChannelCapacity (100) is in effect afterward: five back-to-back enqueues all
            // succeed immediately, with nothing blocking.
            await pipeline.StopAsync(CancellationToken.None);
            await pipeline.StartAsync(CancellationToken.None);

            List<Task<byte[]>> fivePostRestartResponses = [];

            for (int i = 0; i < 5; i++)
            {
                TaskCompletionSource<byte[]> response = new();
                fivePostRestartResponses.Add(response.Task);

                Task enqueueTask = pipeline
                    .EnqueueAsync(
                        new DnsInboundMessage(
                            DnsMessageWriter.Write(BuildQuery()),
                            DnsTransportProtocolEnum.Udp,
                            RemoteAddress,
                            (bytes, token) => Capture(response, bytes, token)
                        )
                    )
                    .AsTask();

                // With a genuine capacity of 100, every one of these five writes must complete
                // immediately, without needing the (still-gated) resolver to drain anything.
                await enqueueTask.WaitAsync(TimeSpan.FromSeconds(2));
            }

            for (int i = 0; i < 5; i++)
                resolver.Gate.Release();

            await Task.WhenAll(fivePostRestartResponses).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (pipeline is not null)
            {
                await pipeline.StopAsync(CancellationToken.None);
                await pipeline.DisposeAsync();
            }

            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Best-effort cleanup only -- a lingering temp file from a test run is harmless.
            }
        }
    }

    /// <summary>
    ///     This method waits for a real configuration reload to actually happen (.NET's own JSON
    ///     configuration provider debounces file-change notifications by a few hundred
    ///     milliseconds) rather than asserting against a fixed delay.
    /// </summary>
    private static async Task WaitForReloadAsync(
        IOptionsMonitor<DnsQueryProcessingPipelineOptions> monitor,
        Func<bool> condition
    )
    {
        CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        while (!condition() && !cts.IsCancellationRequested)
            await Task.Delay(50);

        Assert.True(condition(), "The configuration reload did not happen within the timeout.");
    }

    /// <summary>
    ///     This method waits for an arbitrary condition (here, a background worker having reached
    ///     a certain point) to become true, polling rather than sleeping a fixed amount.
    /// </summary>
    private static async Task WaitForConditionAsync(Func<bool> condition)
    {
        CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        while (!condition() && !cts.IsCancellationRequested)
            await Task.Delay(20);

        Assert.True(condition(), "The expected condition was never reached within the timeout.");
    }
}
