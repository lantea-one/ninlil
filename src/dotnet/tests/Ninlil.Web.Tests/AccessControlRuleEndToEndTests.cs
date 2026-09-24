using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.AccessControl;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class is the closing, real end-to-end proof of the per-client/network access-control
///     engine: not a unit test of <c>DnsAccessControlEngine</c>/<c>DnsAccessControlRuleCache</c> in
///     isolation (that already exists, in the dedicated <c>Ninlil.Dns.Server.AccessControl.Tests</c>
///     project), but a genuine <c>POST /api/access-control-rules</c> against this compiled, running
///     host, followed by a real DNS query over a real UDP socket to this same host's real Plain DNS
///     listener (<see cref="NinlilDnsWebApplicationFactory.PlainDnsPort" />, the exact
///     <see cref="System.Net.Sockets.Socket" />-based listener <see cref="DnsWiringTests" /> already
///     proves is wired end to end) -- proving a client whose real packet source address matches a
///     rule's own network is actually granted recursion while
///     <c>RecursiveServerOptions.EnableRecursion</c> stays globally off for the whole factory (its
///     compiled-in default, never overridden by <see cref="NinlilDnsWebApplicationFactory" />),
///     and a non-matching client is still refused exactly as before this rule ever existed.
///     <para>
///         <b>Distinguishing "matching" from "non-matching" by a real source address</b>, not a
///         constructed <c>DnsQueryContext</c>: <c>127.0.0.0/8</c> is entirely loopback on Linux, so
///         a <see cref="UdpClient" /> whose own local endpoint is explicitly bound to a second
///         address in that block (<c>127.0.0.9</c>) sends a packet the kernel genuinely delivers
///         with that address as its source -- no raw sockets, no spoofing, and no dependency on
///         this test host actually having a second real network interface.
///     </para>
///     <para>
///         <b>Why this waits on the real <see cref="DnsAccessControlRuleCache" /> singleton
///         directly, rather than a fixed delay or a query-based retry loop</b>: the cache reload
///         this rule's own creation triggers happens off the event bus, asynchronously (a real
///         committed benchmark observed a single-digit-millisecond reload latency, but gives no
///         hard SLA, mirroring <c>AuthoritativeZoneCache</c>'s own same posture) -- polling
///         <see cref="DnsAccessControlRuleCache.HasCapability" /> on the real singleton this same
///         running host's own dependency-injection container resolves is a deterministic way to
///         know the write has actually propagated before sending the one real DNS query this test
///         needs an unambiguous answer from, without conflating "the cache has not reloaded yet"
///         with "a real recursive resolution is simply taking a while" (a real query's own overall
///         time budget is up to <c>RecursiveServerOptions.OverallQueryTimeout</c>'s own 15-second
///         default, far longer than the reload itself ever takes).
///     </para>
///     <para>
///         <b>Real internet egress is available in this project's own CI/build environment</b>
///         (confirmed directly, not assumed, before writing this test) -- so the matching client's
///         query is expected to reach a real, successful answer via this host's real, unmodified
///         recursive resolver and root hints; this test's own assertion is deliberately the
///         weaker, environment-independent bar this class's own closing-verification
///         requirement explicitly allows ("or at minimum is not refused for the recursion-disabled
///         reason") -- any response code other than <see cref="DnsResponseCodeEnum.Refused" />
///         (a real answer, or <see cref="DnsResponseCodeEnum.ServerFailure" /> if this particular
///         run's own network happens to be unavailable) is accepted as proof the access-control
///         gate, not the global toggle, is what let this query through at all.
///     </para>
/// </summary>
[Collection(WebTestCollection.Name)]
public class AccessControlRuleEndToEndTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     A second loopback address, distinct from <see cref="IPAddress.Loopback" />, used as the
    ///     "matching" client's own real packet source address -- see this class's own doc comment.
    /// </summary>
    private static readonly IPAddress MatchingAddress = IPAddress.Parse("127.0.0.9");

    [Fact]
    public async Task ARuleGrantingRecursionAccess_LetsOnlyItsMatchingClientRecurseWhileRecursionStaysGloballyOff()
    {
        // Accessing the factory's own Server property is what actually starts the host (and every
        // IHostedService it owns, including the real Plain DNS listener and
        // DnsAccessControlRuleCache itself) -- the same trigger DnsWiringTests already relies on.
        _ = factory.Server;

        HttpClient client = await CreateAuthenticatedClientAsync();

        string ruleName = $"e2e-recursion-{Guid.NewGuid():N}";
        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/access-control-rules",
            new
            {
                name = ruleName,
                cidr = $"{MatchingAddress}/32",
                capabilities = new[] { "RecursionAccess" },
            }
        );
        createResponse.EnsureSuccessStatusCode();
        DnsAccessControlRuleDetail rule =
            await createResponse.Content.ReadFromJsonAsync<DnsAccessControlRuleDetail>();

        try
        {
            // Wait for the real, running singleton cache -- not a mock, not a second instance --
            // to actually observe the write before sending a real query, per this class's own doc
            // comment on why this is deterministic rather than a fixed delay.
            DnsAccessControlRuleCache cache =
                factory.Services.GetRequiredService<DnsAccessControlRuleCache>();
            await WaitUntilAsync(
                () =>
                    cache.HasCapability(
                        MatchingAddress,
                        DnsAccessControlCapabilityEnum.RecursionAccess
                    ),
                TimeSpan.FromSeconds(10)
            );

            // The matching client -- real packet source address 127.0.0.9, covered by the rule
            // above -- is not refused: the access-control engine grants it recursion even though
            // RecursiveServerOptions.EnableRecursion is globally off for this whole factory.
            DnsResponseCodeEnum matchingResponseCode = await SendQueryFromAsync(MatchingAddress);
            Assert.NotEqual(DnsResponseCodeEnum.Refused, matchingResponseCode);

            // The non-matching client -- ordinary loopback, covered by no rule at all -- is still
            // refused, exactly as DnsWiringTests already proves for this same host with no rule in
            // play whatsoever: the global toggle, not the access-control engine, still governs it.
            DnsResponseCodeEnum nonMatchingResponseCode = await SendQueryFromAsync(
                IPAddress.Loopback
            );
            Assert.Equal(DnsResponseCodeEnum.Refused, nonMatchingResponseCode);
        }
        finally
        {
            // Restore this shared factory's own access-control rule set to empty, since every
            // other test class in this collection reuses the exact same running instance.
            await client.DeleteAsync($"/api/access-control-rules/{rule.Id}");
        }
    }

    /// <summary>
    ///     This method sends one real, well-formed recursion-desired query over a real UDP socket
    ///     explicitly bound to <paramref name="sourceAddress" /> as its own local endpoint, to this
    ///     shared host's real Plain DNS listener, and returns the real response code that comes
    ///     back.
    /// </summary>
    private async Task<DnsResponseCodeEnum> SendQueryFromAsync(IPAddress sourceAddress)
    {
        DnsMessage query = new()
        {
            Id = (ushort)Random.Shared.Next(1, ushort.MaxValue),
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
        byte[] queryBytes = DnsMessageWriter.Write(query);

        using UdpClient udpClient = new(new IPEndPoint(sourceAddress, 0));
        await udpClient.SendAsync(
            queryBytes,
            queryBytes.Length,
            new IPEndPoint(IPAddress.Loopback, factory.PlainDnsPort)
        );

        // A generous timeout: a real recursive resolution (root hints -> TLD -> authoritative,
        // plus DNSSEC validation) can genuinely take several seconds, well within
        // RecursiveServerOptions.OverallQueryTimeout's own 15-second default, which upper-bounds
        // how long this host itself will ever take before answering SERVFAIL on its own.
        UdpReceiveResult result = await udpClient
            .ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(20));
        DnsMessage response = DnsMessageReader.Read(result.Buffer);
        return response.ResponseCode;
    }

    /// <summary>
    ///     This method polls <paramref name="condition" /> until it reports <see langword="true" />
    ///     or <paramref name="timeout" /> elapses, failing the test with a clear message in the
    ///     latter case rather than silently proceeding to a query that would only fail for a
    ///     confusing, unrelated reason.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.Fail(
            $"The real DnsAccessControlRuleCache singleton never observed the expected rule within {timeout}."
        );
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            )
        );
        loginResponse.EnsureSuccessStatusCode();

        LoginResponse login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            login.AccessToken
        );

        return client;
    }
}
