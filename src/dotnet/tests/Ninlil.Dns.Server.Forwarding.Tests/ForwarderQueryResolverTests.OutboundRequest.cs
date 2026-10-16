using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.ClientSubnet;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Forwarding.Caching;
using Ninlil.Dns.Server.Forwarding.Tests.Fakes;
using Ninlil.Dns.Server.Recursive;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This part of <see cref="ForwarderQueryResolverTests" /> tests the query
///     <see cref="ForwarderQueryResolver" /> builds for the upstream hop and the flags of the
///     reply it returns. A fake upstream on a loopback UDP port records the datagram it receives,
///     which the tests decode and inspect (EDNS options, DO, CD, TSIG), and the reply the client
///     gets is checked for AA and RA, for a fresh answer and for a cache hit.
/// </summary>
public sealed partial class ForwarderQueryResolverTests
{
    private static readonly byte[] CookieBytes = [1, 2, 3, 4, 5, 6, 7, 8];

    /// <summary>
    ///     This method builds a query with EDNS carrying a cookie, padding and NSID option next to
    ///     the given DO bit and payload size, as a stub resolver might send.
    /// </summary>
    private static DnsMessage BuildEdnsQuery(ushort id, bool dnssecOk, ushort payloadSize)
    {
        DnsMessage request = BuildQuery(id, "example.com.");
        request.Edns = new DnsEdnsOptions(
            0,
            payloadSize,
            dnssecOk,
            [
                new DnsEdnsOption((ushort)EdnsOptionCodeEnum.Cookie, CookieBytes),
                new DnsEdnsOption(12, new byte[16]),
                new DnsEdnsOption((ushort)EdnsOptionCodeEnum.NameServerIdentifier, []),
            ]
        );

        return request;
    }

    /// <summary>
    ///     This method forwards <paramref name="request" /> through a resolver whose only upstream
    ///     is a fake, and returns the query the fake received, decoded.
    /// </summary>
    private async Task<DnsMessage> ForwardAndCaptureAsync(
        DnsMessage request,
        RecursiveServerOptions recursiveOptions = null,
        EdnsClientSubnetService clientSubnetService = null
    )
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.10"))
        );
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");
        ForwarderQueryResolver resolver = CreateResolver(
            recursiveOptions: recursiveOptions,
            clientSubnetService: clientSubnetService
        );

        DnsQueryResolution resolution = await resolver.TryResolveAsync(BuildContext(request));

        Assert.True(resolution.IsHandled);
        Assert.NotNull(upstream.LastQuery);

        return DnsMessageReader.Read(upstream.LastQuery);
    }

    private static EdnsClientSubnetService EnabledClientSubnetService() =>
        new(
            new StaticOptionsMonitor<EdnsClientSubnetOptions>(
                new EdnsClientSubnetOptions { Enable = true }
            )
        );

    [Fact]
    public async Task TryResolveAsync_WithValidationOff_RelaysNoClientEdnsOptionButKeepsDoAndPayload()
    {
        DnsMessage forwarded = await ForwardAndCaptureAsync(BuildEdnsQuery(7, true, 4096));

        Assert.NotNull(forwarded.Edns);
        Assert.True(forwarded.Edns.DnssecOk);
        Assert.Equal(4096, forwarded.Edns.RequestorPayloadSize);
        Assert.Empty(forwarded.Edns.Options);
    }

    [Fact]
    public async Task TryResolveAsync_WithTheEcsServiceOn_SendsTheBuiltEcsAsTheOnlyOption()
    {
        DnsMessage forwarded = await ForwardAndCaptureAsync(
            BuildEdnsQuery(8, false, 1232),
            clientSubnetService: EnabledClientSubnetService()
        );

        DnsEdnsOption only = Assert.Single(forwarded.Edns.Options);
        Assert.Equal((ushort)EdnsOptionCodeEnum.ClientSubnet, only.Code);
        Assert.False(forwarded.Edns.DnssecOk);
    }

    [Fact]
    public async Task TryResolveAsync_WithValidationOffAndNoClientEdns_SendsNoOptRecord()
    {
        DnsMessage forwarded = await ForwardAndCaptureAsync(BuildQuery(9, "example.com."));

        Assert.Null(forwarded.Edns);
        Assert.False(forwarded.CheckingDisabled);
    }

    [Fact]
    public async Task TryResolveAsync_WithTheEcsServiceOnAndNoClientEdns_SendsAnOptWithOnlyEcs()
    {
        DnsMessage forwarded = await ForwardAndCaptureAsync(
            BuildQuery(10, "example.com."),
            clientSubnetService: EnabledClientSubnetService()
        );

        Assert.NotNull(forwarded.Edns);
        Assert.Equal(
            (ushort)EdnsOptionCodeEnum.ClientSubnet,
            Assert.Single(forwarded.Edns.Options).Code
        );
    }

    [Fact]
    public async Task TryResolveAsync_WithTheClientCheckingDisabled_PassesCdUpstream()
    {
        DnsMessage request = BuildQuery(11, "example.com.");
        request.CheckingDisabled = true;

        DnsMessage forwarded = await ForwardAndCaptureAsync(request);

        Assert.True(forwarded.CheckingDisabled);
    }

    [Fact]
    public async Task TryResolveAsync_WithValidationOn_SendsDoAndCdAndNoClientOptions()
    {
        DnsMessage forwarded = await ForwardAndCaptureAsync(
            BuildEdnsQuery(12, false, 1400),
            new RecursiveServerOptions { EnableDnssecValidation = true }
        );

        Assert.True(forwarded.CheckingDisabled);
        Assert.True(forwarded.Edns.DnssecOk);
        Assert.Equal(1400, forwarded.Edns.RequestorPayloadSize);
        Assert.Empty(forwarded.Edns.Options);
    }

    [Fact]
    public async Task TryResolveAsync_WithValidationOnAndNoClientEdns_SendsAnOptWithDo()
    {
        DnsMessage forwarded = await ForwardAndCaptureAsync(
            BuildQuery(13, "example.com."),
            new RecursiveServerOptions { EnableDnssecValidation = true }
        );

        Assert.True(forwarded.Edns.DnssecOk);
        Assert.Equal(
            DnsQueryDispatcher.DefaultUdpPayloadSizeOctets,
            forwarded.Edns.RequestorPayloadSize
        );
    }

    /// <summary>
    ///     This method builds a canned upstream reply with the given AA and RA bits.
    /// </summary>
    private static byte[] BuildResponseWithFlags(bool authoritative, bool recursionAvailable)
    {
        DnsMessage response = DnsMessageReader.Read(
            BuildPositiveResponse(IPAddress.Parse("192.0.2.10"))
        );
        response.IsAuthoritativeAnswer = authoritative;
        response.RecursionAvailable = recursionAvailable;

        return DnsMessageWriter.Write(response);
    }

    [Fact]
    public async Task TryResolveAsync_WithAnUpstreamAnswerMarkedAuthoritative_ClearsAaOnTheReply()
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(BuildResponseWithFlags(true, true));
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");

        DnsQueryResolution resolution = await CreateResolver()
            .TryResolveAsync(BuildContext(BuildQuery(14, "example.com.")));

        Assert.False(resolution.Response.IsAuthoritativeAnswer);
    }

    [Fact]
    public async Task TryResolveAsync_WithAnUpstreamAnswerWithoutRa_SetsRaOnTheReply()
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(
            BuildResponseWithFlags(false, false)
        );
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");

        DnsQueryResolution resolution = await CreateResolver()
            .TryResolveAsync(BuildContext(BuildQuery(15, "example.com.")));

        Assert.True(resolution.Response.RecursionAvailable);
    }

    [Fact]
    public async Task TryResolveAsync_WithACacheHit_ClearsAaAndSetsRaToo()
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(BuildResponseWithFlags(true, false));
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");
        ForwarderQueryResolver resolver = CreateCachingResolver(CreateResponseCache());

        await resolver.TryResolveAsync(BuildContext(BuildQuery(16, "example.com.")));
        DnsQueryResolution cached = await resolver.TryResolveAsync(
            BuildContext(BuildQuery(17, "example.com."))
        );

        Assert.Equal(1, upstream.QueryCount);
        Assert.False(cached.Response.IsAuthoritativeAnswer);
        Assert.True(cached.Response.RecursionAvailable);
    }
}
