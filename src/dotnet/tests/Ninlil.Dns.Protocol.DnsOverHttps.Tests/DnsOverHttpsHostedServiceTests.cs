using System.Buffers.Text;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.DnsOverHttps.Tests.Fakes;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps.Tests;

/// <summary>
///     This class tests DNS over HTTPS (RFC 8484) through <see cref="DnsOverHttpsHostedService" />,
///     which runs its own Kestrel server, over real HTTP/2 connections: the POST and GET forms,
///     HTTP/2 negotiation, allowed TLS protocols and the error statuses. The resolver is a fake
///     that answers every query; resolution is covered elsewhere.
/// </summary>
public sealed class DnsOverHttpsHostedServiceTests : IAsyncLifetime
{
    private static readonly int TestPort = GetFreePort();
    private static readonly IPAddress ExpectedAnswer = IPAddress.Parse("192.0.2.124");

    private DnsQueryProcessingPipeline _pipeline;
    private DnsOverHttpsHostedService _service;
    private HttpClient _client;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        DnsQueryDispatcher dispatcher = new(
            [new AlwaysAnswerAResolver(ExpectedAnswer)],
            new NoOpEventPublisher()
        );

        _pipeline = new DnsQueryProcessingPipeline(
            dispatcher,
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance
        );
        await _pipeline.StartAsync(CancellationToken.None);

        DnsOverHttpsOptions options = new() { Enable = true, Port = TestPort };

        _service = new DnsOverHttpsHostedService(
            _pipeline,
            Options.Create(options),
            NullLogger<DnsOverHttpsHostedService>.Instance
        );
        await _service.StartAsync(CancellationToken.None);

        HttpClientHandler handler = new()
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        _client = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://127.0.0.1:{TestPort}/dns-query"),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _service.StopAsync(CancellationToken.None);
        await _pipeline.StopAsync(CancellationToken.None);
        await _pipeline.DisposeAsync();
    }

    private static DnsMessage BuildQuery(ushort id, string name) =>
        new()
        {
            Id = id,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse(name),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };

    [Fact]
    public async Task PostWithAWireFormatBodyReturnsAWireFormatResponse()
    {
        byte[] queryBytes = DnsMessageWriter.Write(BuildQuery(1, "doh-post.example."));
        using ByteArrayContent content = new(queryBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");

        using HttpResponseMessage httpResponse = await _client.PostAsync("", content);

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        Assert.Equal(
            "application/dns-message",
            httpResponse.Content.Headers.ContentType?.MediaType
        );

        DnsMessage response = DnsMessageReader.Read(
            await httpResponse.Content.ReadAsByteArrayAsync(),
            out _
        );
        Assert.Single(response.Answers);
        Assert.Equal(ExpectedAnswer, new IPAddress(response.Answers[0].Data));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AnAnswer_CarriesAContentLengthHeaderEqualToItsBody(int majorVersion)
    {
        // A client that reads the header before the body (dig 9.20) needs the length up front;
        // without it HTTP/1.1 falls back to chunked encoding and HTTP/2 sends no length at all.
        byte[] queryBytes = DnsMessageWriter.Write(BuildQuery(3, "doh-length.example."));
        using ByteArrayContent content = new(queryBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");
        using HttpRequestMessage request = new(HttpMethod.Post, "")
        {
            Content = content,
            Version = new Version(majorVersion, majorVersion == 1 ? 1 : 0),
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };

        using HttpResponseMessage httpResponse = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead
        );
        byte[] body = await httpResponse.Content.ReadAsByteArrayAsync();

        Assert.Equal(new Version(majorVersion, majorVersion == 1 ? 1 : 0), httpResponse.Version);
        Assert.False(httpResponse.Headers.TransferEncodingChunked ?? false);
        Assert.Equal(body.Length, httpResponse.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task GetWithABase64UrlEncodedQueryReturnsTheSameAnswer()
    {
        byte[] queryBytes = DnsMessageWriter.Write(BuildQuery(2, "doh-get.example."));
        string encoded = Base64Url.EncodeToString(queryBytes);

        using HttpResponseMessage httpResponse = await _client.GetAsync($"?dns={encoded}");

        Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        DnsMessage response = DnsMessageReader.Read(
            await httpResponse.Content.ReadAsByteArrayAsync(),
            out _
        );
        Assert.Equal(ExpectedAnswer, new IPAddress(response.Answers[0].Data));
    }

    [Fact]
    public async Task RestrictingAllowedProtocolsToTls12AndTls13StillHandshakesSuccessfully()
    {
        int restrictedPort = GetFreePort();

        DnsQueryDispatcher dispatcher = new(
            [new AlwaysAnswerAResolver(ExpectedAnswer)],
            new NoOpEventPublisher()
        );
        DnsQueryProcessingPipeline pipeline = new(
            dispatcher,
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance
        );
        await pipeline.StartAsync(CancellationToken.None);

        DnsOverHttpsHostedService service = new(
            pipeline,
            Options.Create(
                new DnsOverHttpsOptions
                {
                    Enable = true,
                    Port = restrictedPort,
                    Certificate = new TlsCertificateOptions
                    {
                        AllowedProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    },
                }
            ),
            NullLogger<DnsOverHttpsHostedService>.Instance
        );

        try
        {
            await service.StartAsync(CancellationToken.None);

            using HttpClientHandler handler = new()
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            };
            using HttpClient client = new(handler)
            {
                BaseAddress = new Uri($"https://127.0.0.1:{restrictedPort}/dns-query"),
            };

            byte[] queryBytes = DnsMessageWriter.Write(
                BuildQuery(1, "doh-restricted-protocols.example.")
            );
            using ByteArrayContent content = new(queryBytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");

            using HttpResponseMessage httpResponse = await client.PostAsync("", content);

            Assert.Equal(HttpStatusCode.OK, httpResponse.StatusCode);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            await pipeline.StopAsync(CancellationToken.None);
            await pipeline.DisposeAsync();
        }
    }

    [Fact]
    public async Task TheConnectionActuallyNegotiatesHttp2NotJustHttp1()
    {
        byte[] queryBytes = DnsMessageWriter.Write(BuildQuery(3, "doh-http2.example."));
        using ByteArrayContent content = new(queryBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");

        using HttpResponseMessage httpResponse = await _client.PostAsync("", content);

        Assert.Equal(2, httpResponse.Version.Major);
    }

    [Fact]
    public async Task PostWithTheWrongContentTypeReturns415()
    {
        using ByteArrayContent content = new([1, 2, 3]);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        using HttpResponseMessage httpResponse = await _client.PostAsync("", content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, httpResponse.StatusCode);
    }

    [Fact]
    public async Task GetWithNoDnsParameterReturns400()
    {
        using HttpResponseMessage httpResponse = await _client.GetAsync("");

        Assert.Equal(HttpStatusCode.BadRequest, httpResponse.StatusCode);
    }

    [Fact]
    public async Task PostWithAnEmptyBodyReturns400()
    {
        using ByteArrayContent content = new([]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/dns-message");

        using HttpResponseMessage httpResponse = await _client.PostAsync("", content);

        Assert.Equal(HttpStatusCode.BadRequest, httpResponse.StatusCode);
    }

    /// <summary>
    ///     This resolver claims every query and answers it with one fixed A record, whatever the
    ///     question; resolution is not under test here.
    /// </summary>
    private sealed class AlwaysAnswerAResolver(IPAddress answer) : IDnsQueryResolver
    {
        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            DnsMessage response = new()
            {
                Id = context.Request.Id,
                IsResponse = true,
                Opcode = context.Request.Opcode,
                IsAuthoritativeAnswer = true,
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
                    answer.GetAddressBytes()
                )
            );

            return ValueTask.FromResult(DnsQueryResolution.Handled(response));
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
    ///     This method asks the operating system for a currently unused port, by binding to port 0
    ///     on loopback and reading back what was assigned. Tests run on shared machines with their
    ///     own DNS, DHCP and web services, and may run alongside another copy of this suite, so no
    ///     test listener uses a fixed or well-known port.
    /// </summary>
    /// <returns>A free TCP port number.</returns>
    private static int GetFreePort()
    {
        using System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}
