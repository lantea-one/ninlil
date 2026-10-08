using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.DnsOverHttps.Tests.Fakes;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Filtering;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.RateLimiting;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps.Tests;

/// <summary>
///     This class tests how <see cref="DnsOverHttpsHostedService" /> answers a query that
///     <see cref="DnsQueryProcessingPipeline" /> drops: 429 when rate limited, 400 for a
///     malformed message, 500 for an internal failure, and an aborted connection for a denied
///     client. It runs a real Kestrel server over HTTPS on a free loopback port, with a fake
///     limiter, filter engine or resolver in the pipeline. Every request must finish within a
///     second, which proves the handler no longer waits for a reply that never comes.
/// </summary>
public sealed class DnsOverHttpsDropTests
{
    private sealed class DenyLimiter : IDnsRateLimiter
    {
        public ValueTask<bool> TryAcquireAsync(
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(false);

        public ValueTask<DnsSubnetRateLimitDecisionEnum> CheckSubnetLimitAsync(
            IPAddress remoteAddress,
            bool isUdp,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(DnsSubnetRateLimitDecisionEnum.Allow);
    }

    private sealed class DenyClientFilterEngine : IDnsFilterEngine
    {
        public ValueTask<DnsFilterDecision> EvaluateClientAsync(
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default,
            string clientId = null
        ) => ValueTask.FromResult(DnsFilterDecision.Denied("test denial"));

        public ValueTask<DnsFilterDecision> EvaluateDomainAsync(
            DnsName queriedName,
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default,
            string clientId = null
        ) => ValueTask.FromResult(DnsFilterDecision.Allowed);

        public ValueTask<DnsFilterDecision> EvaluateResponseAsync(
            DnsMessage response,
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default,
            string clientId = null
        ) => ValueTask.FromResult(DnsFilterDecision.Allowed);

        public DnsMessage BuildDeniedResponse(DnsMessage request) => request;
    }

    private sealed class ThrowingResolver : IDnsQueryResolver
    {
        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        ) => throw new InvalidOperationException("resolver failure");
    }

    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }

    private static byte[] BuildQueryBytes() =>
        DnsMessageWriter.Write(
            new DnsMessage
            {
                Id = 7,
                RecursionDesired = true,
                Questions =
                {
                    new DnsQuestion(
                        DnsName.Parse("drop.example."),
                        DnsResourceRecordTypeEnum.A,
                        DnsClassEnum.Internet
                    ),
                },
            }
        );

    /// <summary>
    ///     This method starts the pipeline and the DoH server, posts the given body, and returns
    ///     the status code, or <see langword="null" /> when the connection was aborted. It also
    ///     returns how long the request took.
    /// </summary>
    private static async Task<(HttpStatusCode? Status, TimeSpan Elapsed)> PostAsync(
        byte[] body,
        IDnsRateLimiter rateLimiter = null,
        IDnsFilterEngine filterEngine = null,
        IDnsQueryResolver resolver = null
    )
    {
        int port = GetFreePort();
        DnsQueryProcessingPipeline pipeline = new(
            new DnsQueryDispatcher([resolver ?? new ThrowingResolver()], new NoOpEventPublisher()),
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions { WorkerCount = 1 }
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance,
            filterEngine,
            rateLimiter
        );
        await pipeline.StartAsync(CancellationToken.None);

        DnsOverHttpsHostedService service = new(
            pipeline,
            Options.Create(new DnsOverHttpsOptions { Enable = true, Port = port }),
            NullLogger<DnsOverHttpsHostedService>.Instance
        );

        try
        {
            await service.StartAsync(CancellationToken.None);

            using HttpClient client = new(
                new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                }
            )
            {
                BaseAddress = new Uri($"https://127.0.0.1:{port}/dns-query"),
                Timeout = TimeSpan.FromSeconds(10),
            };
            using ByteArrayContent content = new(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");

            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                using HttpResponseMessage response = await client.PostAsync("", content);
                return (response.StatusCode, stopwatch.Elapsed);
            }
            catch (HttpRequestException)
            {
                return (null, stopwatch.Elapsed);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            await pipeline.StopAsync(CancellationToken.None);
            await pipeline.DisposeAsync();
        }
    }

    private static int GetFreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [Fact]
    public async Task ARateLimitedQueryGets429Promptly()
    {
        (HttpStatusCode? status, TimeSpan elapsed) = await PostAsync(
            BuildQueryBytes(),
            rateLimiter: new DenyLimiter()
        );

        Assert.Equal(HttpStatusCode.TooManyRequests, status);
        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"took {elapsed}");
    }

    [Fact]
    public async Task AMalformedMessageGets400Promptly()
    {
        (HttpStatusCode? status, TimeSpan elapsed) = await PostAsync([1, 2, 3]);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"took {elapsed}");
    }

    [Fact]
    public async Task ADeniedClientConnectionIsAbortedWithNoResponsePromptly()
    {
        (HttpStatusCode? status, TimeSpan elapsed) = await PostAsync(
            BuildQueryBytes(),
            filterEngine: new DenyClientFilterEngine()
        );

        Assert.Null(status);
        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"took {elapsed}");
    }

    [Fact]
    public async Task AnInternalFailureGets500Promptly()
    {
        (HttpStatusCode? status, TimeSpan elapsed) = await PostAsync(BuildQueryBytes());

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"took {elapsed}");
    }
}
