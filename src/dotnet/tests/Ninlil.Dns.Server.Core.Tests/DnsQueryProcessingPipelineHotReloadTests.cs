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
///     This class checks which pipeline options apply without a restart. It runs a
///     <see cref="DnsQueryProcessingPipeline" /> against a reload-on-change JSON file and writes
///     settings through <see cref="Server.Presets.PresetConfigurationFileApplier" /> (what
///     <c>Ninlil.Web.Controllers.DnsPipelineSettingsController.Patch</c> calls). A change to
///     <see cref="DnsQueryProcessingPipelineOptions.MaxUdpAmplificationFactor" /> affects the next
///     query. A change to <see cref="DnsQueryProcessingPipelineOptions.ChannelCapacity" /> has no
///     effect until <see cref="DnsQueryProcessingPipeline.StopAsync" /> and
///     <see cref="DnsQueryProcessingPipeline.StartAsync" /> run on the same instance, as
///     <c>POST /api/services/query-processing-pipeline/restart</c> does.
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
    ///     This resolver blocks each call until the test releases <see cref="Gate" />, so a test
    ///     controls how many queries are in progress without relying on timing.
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
            // A no-EDNS query is always capped at 512 bytes, whatever the amplification factor,
            // so the query advertises a larger EDNS payload to make the factor the binding limit.
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

            // A tight amplification cap: a 50-record answer will be truncated.
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

            // The same write DnsPipelineSettingsController.Patch makes, loosening the cap so the
            // same answer fits.
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

            // Poll the value itself rather than trusting one OnChange: a late notification from
            // the first write can fire first and made this test flaky.
            await WaitForReloadAsync(
                monitor,
                () => monitor.CurrentValue.MaxUdpAmplificationFactor == 1000.0
            );

            // The same running pipeline now stops truncating, so the cap is read per query.
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
                // Best-effort cleanup; a leftover temp file is harmless.
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

            // Wait until the single worker has taken query A and is blocked on the gate, so the
            // channel is empty again.
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

            // The one-slot channel now holds query B and the worker is stuck on A, so a third
            // enqueue must wait.
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

            // The same write DnsPipelineSettingsController.Patch makes, raising ChannelCapacity
            // while the pipeline runs.
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

            // Poll the value itself rather than trusting one OnChange: a late notification from
            // the first write can fire first and made this test flaky.
            await WaitForReloadAsync(monitor, () => monitor.CurrentValue.ChannelCapacity == 100);

            // The options now say 100, but the running channel was built with capacity 1 and is
            // only rebuilt on restart, so the third enqueue is still waiting.
            await Task.Delay(200);
            Assert.False(
                enqueueCTask.IsCompleted,
                "ChannelCapacity must not hot-reload -- it is only re-read on the next StartAsync."
            );

            // Release query A so the worker takes query B, freeing the slot query C waits for.
            resolver.Gate.Release();
            await responseA.Task.WaitAsync(TimeSpan.FromSeconds(5));
            resolver.Gate.Release();
            await responseB.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await enqueueCTask.WaitAsync(TimeSpan.FromSeconds(5));
            resolver.Gate.Release();
            await responseC.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // Restart the pipeline, as POST /api/services/query-processing-pipeline/restart does,
            // and check the new capacity: five enqueues in a row complete at once.
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

                // With capacity 100, each write completes without the gated resolver draining
                // anything.
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
                // Best-effort cleanup; a leftover temp file is harmless.
            }
        }
    }

    /// <summary>
    ///     This method waits for the configuration reload event, since the JSON provider debounces
    ///     file-change notifications, instead of a fixed delay.
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
    ///     This method polls until a condition holds (such as a worker reaching a point) instead of
    ///     sleeping a fixed time.
    /// </summary>
    private static async Task WaitForConditionAsync(Func<bool> condition)
    {
        CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        while (!condition() && !cts.IsCancellationRequested)
            await Task.Delay(20);

        Assert.True(condition(), "The expected condition was never reached within the timeout.");
    }
}
