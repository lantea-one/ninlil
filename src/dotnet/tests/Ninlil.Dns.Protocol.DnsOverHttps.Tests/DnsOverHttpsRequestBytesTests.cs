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
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps.Tests;

/// <summary>
///     This class tests which bytes a DNS-over-HTTPS POST hands the pipeline. The pipeline keeps
///     the request array for the life of the query, so each request needs an array of its own with
///     exactly the body in it, whether the body has a declared length or arrives chunked. A real
///     Kestrel server and a real pipeline run behind a resolver that parks each query, so two
///     requests are in flight at once, and it records the raw message of every query. The reply
///     path is checked too: the answer reaches the client intact when the reply array is passed on
///     without a copy.
/// </summary>
public sealed class DnsOverHttpsRequestBytesTests
{
    [Fact]
    public async Task APostWithADeclaredLength_ReachesThePipelineAsExactlyItsBody_AndIsAnswered()
    {
        await using Host host = await Host.StartAsync();
        byte[] body = BuildQueryBytes("declared.example.");

        using ByteArrayContent content = new(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
        Assert.Equal(body.Length, content.Headers.ContentLength);

        using HttpResponseMessage response = await host.Client.PostAsync("", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        DnsMessage reply = DnsMessageReader.Read(await response.Content.ReadAsByteArrayAsync());
        Assert.Single(reply.Answers);
        Assert.Equal(body, Assert.Single(host.Resolver.Seen));
    }

    [Fact]
    public async Task AChunkedPost_ReachesThePipelineAsExactlyItsBody_AndIsAnswered()
    {
        await using Host host = await Host.StartAsync();
        byte[] body = BuildQueryBytes("chunked.example.");

        // A stream content without a known length is sent chunked, so the request has no
        // Content-Length and the handler has to buffer the body.
        using StreamContent content = new(new NonSeekableStream(body));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
        Assert.Null(content.Headers.ContentLength);

        using HttpResponseMessage response = await host.Client.PostAsync("", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(DnsMessageReader.Read(await response.Content.ReadAsByteArrayAsync()).Answers);
        Assert.Equal(body, Assert.Single(host.Resolver.Seen));
    }

    [Fact]
    public async Task TwoPostsInFlightAtOnce_EachKeepTheirOwnBytesUntilTheyAreAnswered()
    {
        await using Host host = await Host.StartAsync(holdQueries: true);
        byte[] first = BuildQueryBytes("first.example.");
        byte[] second = BuildQueryBytes("second-longer-name.example.");

        Task<HttpResponseMessage> firstRequest = PostAsync(host.Client, first);
        await host.Resolver.WaitForQueriesAsync(1);
        Task<HttpResponseMessage> secondRequest = PostAsync(host.Client, second);
        await host.Resolver.WaitForQueriesAsync(2);

        // Both queries are parked in the resolver; neither array may have been reused or changed
        // by the arrival of the other request.
        byte[][] held = host.Resolver.Held();
        Assert.NotSame(held[0], held[1]);
        Assert.Equal(first, held[0]);
        Assert.Equal(second, held[1]);

        host.Resolver.ReleaseAll();
        using HttpResponseMessage firstResponse = await firstRequest;
        using HttpResponseMessage secondResponse = await secondRequest;

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(
            "first.example.",
            DnsMessageReader
                .Read(await firstResponse.Content.ReadAsByteArrayAsync())
                .Questions[0]
                .Name.ToString()
        );
        Assert.Equal(
            "second-longer-name.example.",
            DnsMessageReader
                .Read(await secondResponse.Content.ReadAsByteArrayAsync())
                .Questions[0]
                .Name.ToString()
        );
    }

    [Fact]
    public async Task APostLongerThanTheSizeLimit_IsStillRejectedWith400()
    {
        await using Host host = await Host.StartAsync(maxMessageSizeBytes: 40);
        byte[] body = BuildQueryBytes("this-name-is-longer-than-the-limit.example.");
        Assert.True(body.Length > 40);

        using ByteArrayContent content = new(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
        using HttpResponseMessage response = await host.Client.PostAsync("", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Resolver.Seen);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, byte[] body)
    {
        ByteArrayContent content = new(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
        return client.PostAsync("", content);
    }

    private static byte[] BuildQueryBytes(string name) =>
        DnsMessageWriter.Write(
            new DnsMessage
            {
                Id = 0,
                RecursionDesired = true,
                Questions =
                {
                    new DnsQuestion(
                        DnsName.Parse(name),
                        DnsResourceRecordTypeEnum.A,
                        DnsClassEnum.Internet
                    ),
                },
            }
        );

    /// <summary>
    ///     This stream serves a byte array and reports that it cannot seek or give its length, so
    ///     an HTTP client sends it as a chunked body.
    /// </summary>
    private sealed class NonSeekableStream(byte[] source) : Stream
    {
        private int _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int taken = Math.Min(count, source.Length - _position);
            Array.Copy(source, _position, buffer, offset, taken);
            _position += taken;
            return taken;
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    /// <summary>
    ///     This resolver answers every query with one A record that repeats the question, records
    ///     the raw message it was given, and, when asked to, parks each query until the test
    ///     releases them.
    /// </summary>
    private sealed class RecordingResolver(bool holdQueries) : IDnsQueryResolver
    {
        private readonly object _gate = new();
        private readonly List<byte[]> _seen = [];
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public IReadOnlyList<byte[]> Seen
        {
            get
            {
                lock (_gate)
                    return [.. _seen];
            }
        }

        public byte[][] Held() => [.. Seen];

        public void ReleaseAll() => _release.TrySetResult();

        /// <summary>
        ///     This method waits until at least <paramref name="count" /> queries have reached the
        ///     resolver.
        /// </summary>
        public async Task WaitForQueriesAsync(int count)
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));

            while (Seen.Count < count)
                await Task.Delay(10, timeout.Token);
        }

        public async ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            lock (_gate)
                _seen.Add(context.RawMessage);

            if (holdQueries)
                await _release.Task.WaitAsync(stoppingToken);

            DnsMessage response = new()
            {
                Id = context.Request.Id,
                IsResponse = true,
                RecursionDesired = context.Request.RecursionDesired,
                ResponseCode = DnsResponseCodeEnum.NoError,
                Questions = { context.Request.Questions[0] },
            };
            response.Answers.Add(
                new DnsResourceRecord(
                    context.Request.Questions[0].Name,
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    60,
                    IPAddress.Parse("192.0.2.77").GetAddressBytes()
                )
            );
            return DnsQueryResolution.Handled(response);
        }
    }

    /// <summary>
    ///     This publisher discards every event; the event bus is not under test here.
    /// </summary>
    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }

    /// <summary>
    ///     This class runs a pipeline over a <see cref="RecordingResolver" /> and a DoH service
    ///     on a free loopback port, and gives the test an HTTP/2 client that accepts the
    ///     service's self-signed certificate.
    /// </summary>
    private sealed class Host : IAsyncDisposable
    {
        private DnsQueryProcessingPipeline _pipeline;
        private DnsOverHttpsHostedService _service;

        public RecordingResolver Resolver { get; private set; }

        public HttpClient Client { get; private set; }

        public static async Task<Host> StartAsync(
            bool holdQueries = false,
            int maxMessageSizeBytes = 65535
        )
        {
            Host host = new() { Resolver = new RecordingResolver(holdQueries) };
            host._pipeline = new DnsQueryProcessingPipeline(
                new DnsQueryDispatcher([host.Resolver], new NoOpEventPublisher()),
                new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                    new DnsQueryProcessingPipelineOptions()
                ),
                NullLogger<DnsQueryProcessingPipeline>.Instance
            );
            await host._pipeline.StartAsync(CancellationToken.None);

            int port = GetFreePort();
            host._service = new DnsOverHttpsHostedService(
                host._pipeline,
                Options.Create(
                    new DnsOverHttpsOptions
                    {
                        Enable = true,
                        Port = port,
                        MaxMessageSizeBytes = maxMessageSizeBytes,
                    }
                ),
                NullLogger<DnsOverHttpsHostedService>.Instance
            );
            await host._service.StartAsync(CancellationToken.None);

            host.Client = new HttpClient(
                new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                }
            )
            {
                BaseAddress = new Uri($"https://127.0.0.1:{port}/dns-query"),
                DefaultRequestVersion = HttpVersion.Version20,
                DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
                Timeout = TimeSpan.FromSeconds(30),
            };
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            Resolver.ReleaseAll();
            Client.Dispose();
            await _service.StopAsync(CancellationToken.None);
            await _pipeline.StopAsync(CancellationToken.None);
            await _pipeline.DisposeAsync();
        }
    }

    private static int GetFreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
