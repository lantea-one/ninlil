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
///     This class checks that an access-control rule changes how DNS queries are answered. It
///     creates a rule through <c>POST /api/access-control-rules</c> on the shared in-process host
///     (signed in as the bootstrap administrator), then sends DNS queries over UDP sockets to the
///     host's Plain DNS listener on <see cref="NinlilDnsWebApplicationFactory.PlainDnsPort" />. A
///     client inside the rule's network is granted recursion while
///     <c>RecursiveServerOptions.EnableRecursion</c> stays off (its default, which the factory
///     does not override), and a client outside it is still refused. The engine and cache are
///     unit-tested in <c>Ninlil.Dns.Server.AccessControl.Tests</c>.
///     <para>
///         The two clients differ by source address, not by a constructed
///         <c>DnsQueryContext</c>. All of <c>127.0.0.0/8</c> is loopback on Linux, so a
///         <see cref="UdpClient" /> bound to <c>127.0.0.9</c> sends from that address without raw
///         sockets or a second network interface.
///     </para>
///     <para>
///         The rule cache reloads asynchronously from the event bus, so the test polls
///         <see cref="DnsAccessControlRuleCache.HasCapability" /> on the host's singleton until it
///         sees the rule. That keeps "the cache has not reloaded yet" apart from "the recursive
///         lookup is slow" (a lookup may take up to <c>RecursiveServerOptions.OverallQueryTimeout</c>,
///         15 seconds by default).
///     </para>
///     <para>
///         The matching client's query is resolved from the root hints, which needs internet
///         access. The test accepts any response code except
///         <see cref="DnsResponseCodeEnum.Refused" />, so a <see cref="DnsResponseCodeEnum.ServerFailure" />
///         on a machine without network access still passes: the access-control gate let the
///         query through, which is what is being checked.
///     </para>
/// </summary>
[Collection(WebTestCollection.Name)]
public class AccessControlRuleEndToEndTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     A second loopback address, distinct from <see cref="IPAddress.Loopback" />, used as the
    ///     matching client's source address (see the class summary).
    /// </summary>
    private static readonly IPAddress MatchingAddress = IPAddress.Parse("127.0.0.9");

    [Fact]
    public async Task ARuleGrantingRecursionAccess_LetsOnlyItsMatchingClientRecurseWhileRecursionStaysGloballyOff()
    {
        // Reading Server starts the host and its hosted services, including the Plain DNS
        // listener and DnsAccessControlRuleCache, as in DnsWiringTests.
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
            // Wait until the host's cache has loaded the rule (see the class summary).
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

            // The client at 127.0.0.9 is inside the rule, so it is granted recursion although
            // RecursiveServerOptions.EnableRecursion is off.
            DnsResponseCodeEnum matchingResponseCode = await SendQueryFromAsync(MatchingAddress);
            Assert.NotEqual(DnsResponseCodeEnum.Refused, matchingResponseCode);

            // The client at 127.0.0.1 matches no rule, so it is still refused, as in
            // DnsWiringTests.
            DnsResponseCodeEnum nonMatchingResponseCode = await SendQueryFromAsync(
                IPAddress.Loopback
            );
            Assert.Equal(DnsResponseCodeEnum.Refused, nonMatchingResponseCode);
        }
        finally
        {
            // Remove the rule, since every other test class shares this host.
            await client.DeleteAsync($"/api/access-control-rules/{rule.Id}");
        }
    }

    /// <summary>
    ///     A rule written as the bare address (no <c>/32</c>) is accepted and grants the
    ///     capability to exactly that client. It stops at the cache check, so it needs no
    ///     recursive lookup.
    /// </summary>
    [Fact]
    public async Task ARuleWrittenAsABareAddress_GrantsItsCapabilityToThatClient()
    {
        _ = factory.Server;

        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/access-control-rules",
            new
            {
                name = $"e2e-bare-{Guid.NewGuid():N}",
                cidr = MatchingAddress.ToString(),
                capabilities = new[] { "RecursionAccess" },
            }
        );
        createResponse.EnsureSuccessStatusCode();
        DnsAccessControlRuleDetail rule =
            await createResponse.Content.ReadFromJsonAsync<DnsAccessControlRuleDetail>();

        try
        {
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

            Assert.False(
                cache.HasCapability(
                    IPAddress.Loopback,
                    DnsAccessControlCapabilityEnum.RecursionAccess
                )
            );
        }
        finally
        {
            await client.DeleteAsync($"/api/access-control-rules/{rule.Id}");
        }
    }

    /// <summary>
    ///     This method sends one recursion-desired query for <c>example.com. A</c> from a UDP
    ///     socket bound to <paramref name="sourceAddress" /> to the host's Plain DNS listener, and
    ///     returns the response code.
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

        // A recursive lookup from the root hints, with DNSSEC validation, can take several
        // seconds. The host answers SERVFAIL after RecursiveServerOptions.OverallQueryTimeout
        // (15 seconds by default), so 20 seconds is enough.
        UdpReceiveResult result = await udpClient
            .ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(20));
        DnsMessage response = DnsMessageReader.Read(result.Buffer);
        return response.ResponseCode;
    }

    /// <summary>
    ///     This method polls <paramref name="condition" /> until it reports <see langword="true" />
    ///     or <paramref name="timeout" /> elapses, and fails the test with a clear message on
    ///     timeout instead of going on to a query that would fail for a confusing reason.
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
