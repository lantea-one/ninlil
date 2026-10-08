using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Net;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.RateLimiting;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.RateLimiting.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.RateLimiting.Tests;

/// <summary>
///     This class tests that <see cref="DnsQueryProcessingPipeline" /> hands the limiter a
///     normalized client address. It runs the pipeline with a real
///     <see cref="TokenBucketDnsRateLimiter" /> and a resolver that answers everything, feeds it
///     IPv4-mapped IPv6 addresses (what a dual-stack socket reports for IPv4 clients), and
///     counts the replies. It sits in this project, not the pipeline's, because it needs the
///     limiter.
/// </summary>
public sealed class DnsQueryProcessingPipelineClientAddressTests
{
    [Fact]
    public async Task AMappedAddressInsideASubnetBypassCidrIsNeverRateLimited()
    {
        await using Harness harness = await Harness.StartAsync(
            new DnsRateLimitOptions
            {
                Enable = true,
                BurstSize = 1000,
                QueriesPerSecond = 1000,
                SubnetUdpQueryLimit = 1,
                SubnetTruncatePercentage = 0,
                SubnetBypassCidrs = ["192.0.2.0/24"],
            }
        );

        IPAddress client = IPAddress.Parse("::ffff:192.0.2.9");

        for (int i = 0; i < 5; i++)
            await harness.QueryAsync(client);

        Assert.Equal(5, harness.RepliesFor(client));
    }

    [Fact]
    public async Task MappedClientsInDifferentIPv4SubnetsGetSeparateSubnetWindows()
    {
        await using Harness harness = await Harness.StartAsync(
            new DnsRateLimitOptions
            {
                Enable = true,
                BurstSize = 1000,
                QueriesPerSecond = 1000,
                SubnetUdpQueryLimit = 1,
                SubnetTruncatePercentage = 0,
            }
        );

        IPAddress first = IPAddress.Parse("::ffff:192.0.2.9");
        IPAddress second = IPAddress.Parse("::ffff:198.51.100.9");

        await harness.QueryAsync(first);
        await harness.QueryAsync(second);

        // Each client is the first in its own /24, so both are answered. When the mapped form was
        // masked as IPv6, every IPv4 client fell into one "::" window and the second was dropped.
        Assert.Equal(1, harness.RepliesFor(first));
        Assert.Equal(1, harness.RepliesFor(second));
        Assert.Equal(2, harness.Limiter.SubnetWindows.Count);
    }

    /// <summary>
    ///     This class owns a started pipeline plus the limiter in front of it, and records which
    ///     client address each reply was sent to.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly DnsQueryProcessingPipeline _pipeline;
        private readonly Dictionary<IPAddress, int> _replies = [];

        public TokenBucketDnsRateLimiter Limiter { get; }

        private Harness(DnsRateLimitOptions options)
        {
            Limiter = new TokenBucketDnsRateLimiter(Options.Create(options));
            _pipeline = new DnsQueryProcessingPipeline(
                new DnsQueryDispatcher([new AnswerEverythingResolver()], new NoOpEventPublisher()),
                new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                    new DnsQueryProcessingPipelineOptions()
                ),
                NullLogger<DnsQueryProcessingPipeline>.Instance,
                rateLimiter: Limiter
            );
        }

        public static async Task<Harness> StartAsync(DnsRateLimitOptions options)
        {
            Harness harness = new(options);
            await harness._pipeline.StartAsync(CancellationToken.None);
            return harness;
        }

        public int RepliesFor(IPAddress client) => _replies.GetValueOrDefault(Normalized(client));

        /// <summary>
        ///     This method enqueues one query and waits until the pipeline has finished with it:
        ///     either it replied, or the limiter dropped it (the call then returns after a short
        ///     settle time, since a drop sends nothing to wait for).
        /// </summary>
        public async Task QueryAsync(IPAddress client)
        {
            TaskCompletionSource replied = new(TaskCreationOptions.RunContinuationsAsynchronously);

            await _pipeline.EnqueueAsync(
                new DnsInboundMessage(
                    DnsMessageWriter.Write(BuildQuery()),
                    DnsTransportProtocolEnum.Udp,
                    client,
                    (bytes, token) =>
                    {
                        lock (_replies)
                            _replies[Normalized(client)] =
                                _replies.GetValueOrDefault(Normalized(client)) + 1;

                        replied.TrySetResult();
                        return ValueTask.CompletedTask;
                    }
                )
            );

            await Task.WhenAny(replied.Task, Task.Delay(300));
        }

        public async ValueTask DisposeAsync()
        {
            await _pipeline.StopAsync(CancellationToken.None);
            await _pipeline.DisposeAsync();
        }

        private static IPAddress Normalized(IPAddress client) => ClientAddress.Normalize(client);
    }

    private static DnsMessage BuildQuery() =>
        new()
        {
            Id = 7,
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
        };

    /// <summary>This resolver answers every query with an empty NOERROR response.</summary>
    private sealed class AnswerEverythingResolver : IDnsQueryResolver
    {
        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        ) =>
            ValueTask.FromResult(
                DnsQueryResolution.Handled(
                    new DnsMessage
                    {
                        Id = context.Request.Id,
                        IsResponse = true,
                        Opcode = context.Request.Opcode,
                        ResponseCode = DnsResponseCodeEnum.NoError,
                        Questions = { context.Request.Questions[0] },
                    }
                )
            );
    }

    /// <summary>This publisher discards every event.</summary>
    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}
