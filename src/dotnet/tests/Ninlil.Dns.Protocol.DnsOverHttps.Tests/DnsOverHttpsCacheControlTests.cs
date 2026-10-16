using System.Buffers.Text;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Protocol.DnsOverHttps.Tests.Fakes;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps.Tests;

/// <summary>
///     This class tests three behaviours of <see cref="DnsOverHttpsRequestHandler" /> through
///     <see cref="DnsOverHttpsHostedService" />: the POST media type match (case-insensitive,
///     parameters ignored, RFC 9110 §8.3.1), the <c>Cache-Control</c> freshness lifetime on GET
///     and POST (RFC 8484 §5.1, RFC 2308 §5), and the address used for a connection without a
///     remote address. One Kestrel server on a free port serves the whole class; a fake resolver
///     picks a canned answer from the queried name.
/// </summary>
public sealed class DnsOverHttpsCacheControlTests : IAsyncLifetime
{
    private const string MultiTtlName = "multi.example.";
    private const string NxDomainName = "nx.example.";
    private const string ServFailName = "fail.example.";

    private DnsQueryProcessingPipeline _pipeline;
    private DnsOverHttpsHostedService _service;
    private HttpClient _client;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        int port = GetFreePort();
        _pipeline = new DnsQueryProcessingPipeline(
            new DnsQueryDispatcher([new CannedResolver()], new NoOpEventPublisher()),
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions { WorkerCount = 1 }
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance
        );
        await _pipeline.StartAsync(CancellationToken.None);

        _service = new DnsOverHttpsHostedService(
            _pipeline,
            Options.Create(new DnsOverHttpsOptions { Enable = true, Port = port }),
            NullLogger<DnsOverHttpsHostedService>.Instance
        );
        await _service.StartAsync(CancellationToken.None);

        _client = new HttpClient(
            new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            }
        )
        {
            BaseAddress = new Uri($"https://127.0.0.1:{port}/dns-query"),
            Timeout = TimeSpan.FromSeconds(10),
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

    /// <summary>
    ///     This fake resolver answers by queried name: <c>multi.example.</c> gets two A records
    ///     with TTLs 300 and 60, <c>nx.example.</c> gets NXDOMAIN with an SOA (TTL 3600, MINIMUM
    ///     900) in the Authority section, and anything else gets SERVFAIL.
    /// </summary>
    private sealed class CannedResolver : IDnsQueryResolver
    {
        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            DnsName name = context.Request.Questions[0].Name;
            DnsMessage response = new()
            {
                Id = context.Request.Id,
                IsResponse = true,
                RecursionDesired = context.Request.RecursionDesired,
                Questions = { context.Request.Questions[0] },
            };

            if (name == DnsName.Parse(MultiTtlName))
            {
                response.ResponseCode = DnsResponseCodeEnum.NoError;
                response.Answers.Add(ARecord(name, 300, "192.0.2.1"));
                response.Answers.Add(ARecord(name, 60, "192.0.2.2"));
            }
            else if (name == DnsName.Parse(NxDomainName))
            {
                response.ResponseCode = DnsResponseCodeEnum.NxDomain;
                response.AuthorityRecords.Add(
                    new DnsResourceRecord(
                        DnsName.Parse("example."),
                        DnsResourceRecordTypeEnum.Soa,
                        DnsClassEnum.Internet,
                        3600,
                        DnsRecordDataCodecRegistry.Encode(
                            DnsResourceRecordTypeEnum.Soa,
                            new SoaRecordData(
                                DnsName.Parse("ns.example."),
                                DnsName.Parse("hostmaster.example."),
                                1,
                                7200,
                                900,
                                86400,
                                900
                            )
                        )
                    )
                );
            }
            else
            {
                response.ResponseCode = DnsResponseCodeEnum.ServerFailure;
            }

            return ValueTask.FromResult(DnsQueryResolution.Handled(response));
        }

        private static DnsResourceRecord ARecord(DnsName name, uint ttl, string address) =>
            new(
                name,
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet,
                ttl,
                IPAddress.Parse(address).GetAddressBytes()
            );
    }

    /// <summary>
    ///     This publisher discards every event; the event bus is not under test here.
    /// </summary>
    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }

    private static byte[] BuildQuery(string name) =>
        DnsMessageWriter.Write(
            new DnsMessage
            {
                Id = 9,
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

    private async Task<HttpResponseMessage> PostAsync(string name, string contentType)
    {
        using ByteArrayContent content = new(BuildQuery(name));

        if (contentType is not null)
        {
            content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        return await _client.PostAsync("", content);
    }

    /// <summary>
    ///     This method asserts the reply is <c>private</c> with the given <c>max-age</c>. The
    ///     client parses the header and may reorder the directives, so the test compares parsed
    ///     values, not text.
    /// </summary>
    private static void AssertPrivateMaxAge(HttpResponseMessage response, int seconds)
    {
        CacheControlHeaderValue cacheControl = response.Headers.CacheControl;

        Assert.NotNull(cacheControl);
        Assert.True(cacheControl.Private);
        Assert.Equal(TimeSpan.FromSeconds(seconds), cacheControl.MaxAge);
    }

    private Task<HttpResponseMessage> GetAsync(string name) =>
        _client.GetAsync($"?dns={Base64Url.EncodeToString(BuildQuery(name))}");

    [Theory]
    [InlineData("application/dns-message; charset=utf-8")]
    [InlineData("Application/DNS-Message")]
    public async Task Post_WithTheDnsMessageMediaTypeInAnyCaseOrWithParameters_Returns200(
        string contentType
    )
    {
        using HttpResponseMessage response = await PostAsync(MultiTtlName, contentType);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/dns-message-extra")]
    [InlineData(null)]
    public async Task Post_WithAnotherMediaTypeOrNone_Returns415(string contentType)
    {
        using HttpResponseMessage response = await PostAsync(MultiTtlName, contentType);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithAnswerTtls300And60_CachesPrivatelyForTheSmallest()
    {
        using HttpResponseMessage response = await PostAsync(
            MultiTtlName,
            "application/dns-message"
        );

        AssertPrivateMaxAge(response, 60);
    }

    [Fact]
    public async Task Get_WithAnswerTtls300And60_CachesPrivatelyForTheSmallest()
    {
        using HttpResponseMessage response = await GetAsync(MultiTtlName);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateMaxAge(response, 60);
    }

    [Fact]
    public async Task Post_WithNxDomainAndSoa_CachesForTheSmallerOfSoaTtlAndMinimum()
    {
        using HttpResponseMessage response = await PostAsync(
            NxDomainName,
            "application/dns-message"
        );

        AssertPrivateMaxAge(response, 900);
    }

    [Fact]
    public async Task Get_WithNxDomainAndSoa_CachesForTheSmallerOfSoaTtlAndMinimum()
    {
        using HttpResponseMessage response = await GetAsync(NxDomainName);

        AssertPrivateMaxAge(response, 900);
    }

    [Fact]
    public async Task Post_WithServerFailure_IsNotCached()
    {
        using HttpResponseMessage response = await PostAsync(
            ServFailName,
            "application/dns-message"
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertPrivateMaxAge(response, 0);
    }

    [Fact]
    public async Task Get_WithServerFailure_IsNotCached()
    {
        using HttpResponseMessage response = await GetAsync(ServFailName);

        AssertPrivateMaxAge(response, 0);
    }

    [Fact]
    public void ClientAddressOf_WithNoRemoteAddress_IsTheUnspecifiedIPv6Address()
    {
        DefaultHttpContext context = new();
        context.Connection.RemoteIpAddress = null;

        Assert.Equal(IPAddress.IPv6None, DnsOverHttpsRequestHandler.ClientAddressOf(context));
    }

    [Fact]
    public void ClientAddressOf_WithARemoteAddress_ReturnsIt()
    {
        DefaultHttpContext context = new();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.77");

        Assert.Equal(
            IPAddress.Parse("192.0.2.77"),
            DnsOverHttpsRequestHandler.ClientAddressOf(context)
        );
    }

    /// <summary>
    ///     This method asks the OS for a free loopback TCP port.
    /// </summary>
    private static int GetFreePort()
    {
        using System.Net.Sockets.TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
