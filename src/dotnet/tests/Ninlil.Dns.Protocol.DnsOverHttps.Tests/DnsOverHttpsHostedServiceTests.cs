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
///     This class exercises RFC 8484's DNS-over-HTTPS transport end-to-end over a real, nested
///     Kestrel host and a real HTTP/2 connection, promoted from this project's original 3e
///     scratch smoke test into a permanent suite, per this project's own "hybrid testing"
///     convention. Like <c>Ninlil.Dns.Protocol.DnsOverTls.Tests</c>, the resolver behind the
///     transport is deliberately a minimal, always-answering fake -- resolution correctness
///     itself is covered elsewhere.
/// </summary>
public sealed class DnsOverHttpsHostedServiceTests : IAsyncLifetime
{
    private const int TestPort = 18443;
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
        const int restrictedPort = TestPort + 1;

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
    ///     This resolver always claims every query and answers it with one fixed A record,
    ///     regardless of the question asked -- deliberately simplified, since resolution
    ///     correctness is not what this test class is verifying.
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
    ///     This publisher discards every event -- the internal event bus itself is already
    ///     covered by <c>Ninlil.Dns.Common.Tests</c>' own 3a coverage.
    /// </summary>
    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}
