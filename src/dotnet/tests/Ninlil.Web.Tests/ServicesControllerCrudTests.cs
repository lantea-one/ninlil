using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Services;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers Service Lifecycle Management's own <c>/api/services</c> REST endpoints
///     (<see cref="ServicesController" />) end to end, against a real, in-process host (see
///     <see cref="NinlilDnsWebApplicationFactory" />) -- listing every registered service, and,
///     critically, proving start/stop/restart is a real, observable, in-process hot lifecycle, not
///     merely a controller returning a plausible-looking <c>200</c>: stopping Plain DNS's own UDP
///     listener through this REST endpoint makes a real UDP query against this test host's own
///     real socket genuinely time out, and starting it again makes the exact same query answer
///     normally, the same "send a real UDP datagram at the real bound port" mechanism
///     <see cref="DnsWiringTests" /> already establishes for confirming this engine is wired up at
///     all.
///     <para>
///         Every test that stops a real service restores it before returning (even though this
///         collection's own tests never run concurrently with each other -- see
///         <see cref="WebTestCollection" />'s own doc comment -- execution order across test
///         classes within the collection is not guaranteed, and other classes, most notably
///         <see cref="DnsWiringTests" /> itself, depend on Plain DNS's own listeners staying up).
///     </para>
/// </summary>
[Collection(WebTestCollection.Name)]
public class ServicesControllerCrudTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This constant contains the exact count of services
    ///     <see cref="Services.ManagedServiceRegistry.Build" /> registers against this test host's
    ///     own default configuration -- 7 DNS transport listeners (Plain UDP/TCP, DNS-over-TLS,
    ///     DNS-over-QUIC, DNSCrypt UDP/TCP, DNS-over-HTTPS) plus 19 background maintenance loops
    ///     (18 pre-existing, plus <c>webhook-delivery-retry-sweep</c>).
    ///     <c>filter-list-external-refresh</c> is included even though filtering is left at its own
    ///     off-by-default configuration for this test host:
    ///     <c>DnsFilterListExternalRefreshService</c> is registered unconditionally (the same
    ///     "always registered, gated internally" shape every other background service in this
    ///     registry already followed) so a live <c>FilteringSettingsController.Patch</c> write can
    ///     turn filtering on with no restart -- see
    ///     <see cref="Services.ManagedServiceRegistry.Build" />'s own doc comment. Asserted directly
    ///     so a future change to the registry's own membership is caught here rather than silently
    ///     drifting unnoticed.
    /// </summary>
    private const int ExpectedServiceCount = 26;

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

    private static DnsMessage BuildQuery(ushort id) =>
        new()
        {
            Id = id,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("services-controller-test.example."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };

    /// <summary>
    ///     This method sends one real UDP datagram to this test host's own real Plain DNS
    ///     listener and reports whether a well-formed answer came back within a short timeout --
    ///     <see langword="false" /> both when the socket is genuinely unreachable (connection
    ///     refused/reset) and when nothing arrives at all before the timeout, either of which is
    ///     the correct observable outcome of the listener being stopped.
    /// </summary>
    private async Task<bool> TryRealUdpQueryAsync()
    {
        DnsMessage query = BuildQuery(9001);
        byte[] queryBytes = DnsMessageWriter.Write(query);

        using UdpClient client = new();
        client.Client.ReceiveTimeout = 2000;

        try
        {
            await client.SendAsync(
                queryBytes,
                queryBytes.Length,
                "127.0.0.1",
                factory.PlainDnsPort
            );
            UdpReceiveResult result = await client
                .ReceiveAsync()
                .WaitAsync(TimeSpan.FromSeconds(2));
            DnsMessage response = DnsMessageReader.Read(result.Buffer);
            return response.Id == query.Id;
        }
        catch (Exception exception)
            when (exception is SocketException or TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    [Fact]
    public async Task List_WithAnAuthenticatedCaller_ReturnsEveryRegisteredService()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/services");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<ManagedServiceSummary> services = await response.Content.ReadFromJsonAsync<
            List<ManagedServiceSummary>
        >();

        Assert.Equal(ExpectedServiceCount, services.Count);

        // Every name is distinct -- a real, checkable structural property, not just "some
        // entries exist".
        Assert.Equal(
            services.Select(service => service.Name).Distinct(StringComparer.Ordinal).Count(),
            services.Count
        );

        // A representative transport and a representative background service, both genuinely
        // running -- this test host's own real generic host already started every one of them
        // before this request was ever made.
        ManagedServiceSummary plainUdp = Assert.Single(
            services,
            service => service.Name == "plain-udp"
        );
        Assert.Equal(ManagedServiceCategoryEnum.Transport, plainUdp.Category);
        Assert.True(plainUdp.IsRunning);

        ManagedServiceSummary pipeline = Assert.Single(
            services,
            service => service.Name == "query-processing-pipeline"
        );
        Assert.Equal(ManagedServiceCategoryEnum.Background, pipeline.Category);
        Assert.True(pipeline.IsRunning);
    }

    [Fact]
    public async Task ServicesEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/services");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Start_Stop_AndRestart_OnAnUnknownServiceName_AllReturnNotFound()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        const string unknownName = "this-service-does-not-exist";

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PostAsync($"/api/services/{unknownName}/start", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PostAsync($"/api/services/{unknownName}/stop", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PostAsync($"/api/services/{unknownName}/restart", null)).StatusCode
        );
    }

    /// <summary>
    ///     This is the real, load-bearing test for this whole phase: stopping Plain DNS's own UDP
    ///     listener through this REST endpoint genuinely unbinds the real socket, so a real UDP
    ///     query against this test host's own real port gets no answer at all -- not merely a
    ///     controller reporting <c>IsRunning: false</c> while the listener keeps quietly working.
    ///     Starting it again genuinely rebinds it, restoring real answers, and this test always
    ///     leaves the listener running when it returns (see this class's own doc comment for why).
    /// </summary>
    [Fact]
    public async Task Stop_OnThePlainUdpTransport_ActuallyStopsAnsweringRealQueries_AndStartResumes()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // Confirm a real answer arrives before touching anything.
        Assert.True(await TryRealUdpQueryAsync());

        try
        {
            HttpResponseMessage stopResponse = await client.PostAsync(
                "/api/services/plain-udp/stop",
                null
            );
            Assert.Equal(HttpStatusCode.OK, stopResponse.StatusCode);
            ManagedServiceSummary stopped =
                await stopResponse.Content.ReadFromJsonAsync<ManagedServiceSummary>();
            Assert.False(stopped.IsRunning);

            // The real socket is genuinely unbound now -- a real UDP datagram gets no answer.
            Assert.False(await TryRealUdpQueryAsync());
        }
        finally
        {
            HttpResponseMessage startResponse = await client.PostAsync(
                "/api/services/plain-udp/start",
                null
            );
            Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
            ManagedServiceSummary started =
                await startResponse.Content.ReadFromJsonAsync<ManagedServiceSummary>();
            Assert.True(started.IsRunning);
        }

        // The real socket answers again.
        Assert.True(await TryRealUdpQueryAsync());
    }

    /// <summary>
    ///     This test covers <see cref="ServicesController.Restart" />'s own stop-then-start
    ///     sequence against Plain DNS's own TCP listener (kept separate from the UDP listener
    ///     <see cref="Stop_OnThePlainUdpTransport_ActuallyStopsAnsweringRealQueries_AndStartResumes" />
    ///     exercises, so a failure in one never masks or interferes with the other) -- confirming
    ///     the socket is genuinely rebound afterward via <see cref="ManagedServiceSummary.IsRunning" />,
    ///     the same real, independently-observed <c>IsListening</c> signal
    ///     <c>TransportListenersHealthCheck</c> already relies on.
    /// </summary>
    [Fact]
    public async Task Restart_OnThePlainTcpTransport_RoundTripsBackToRunning()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage restartResponse = await client.PostAsync(
            "/api/services/plain-tcp/restart",
            null
        );

        Assert.Equal(HttpStatusCode.OK, restartResponse.StatusCode);
        ManagedServiceSummary restarted =
            await restartResponse.Content.ReadFromJsonAsync<ManagedServiceSummary>();
        Assert.Equal("plain-tcp", restarted.Name);
        Assert.Equal(ManagedServiceCategoryEnum.Transport, restarted.Category);
        Assert.True(restarted.IsRunning);
    }

    /// <summary>
    ///     This test covers a background maintenance loop with no real socket-level signal of its
    ///     own -- <see cref="ManagedServiceSummary.IsRunning" /> here falls back to
    ///     <see cref="ManagedServiceRegistration" />'s own start/stop bookkeeping (see that class's
    ///     own doc comment), so this confirms that bookkeeping genuinely flips both directions
    ///     through this REST endpoint, not just that the endpoint returns <c>200</c>.
    /// </summary>
    [Fact]
    public async Task Stop_ThenStart_OnTheQueryLogCleanupBackgroundService_TogglesIsRunning()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage stopResponse = await client.PostAsync(
            "/api/services/query-log-cleanup/stop",
            null
        );
        Assert.Equal(HttpStatusCode.OK, stopResponse.StatusCode);
        ManagedServiceSummary stopped =
            await stopResponse.Content.ReadFromJsonAsync<ManagedServiceSummary>();
        Assert.Equal(ManagedServiceCategoryEnum.Background, stopped.Category);
        Assert.False(stopped.IsRunning);

        HttpResponseMessage startResponse = await client.PostAsync(
            "/api/services/query-log-cleanup/start",
            null
        );
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        ManagedServiceSummary started =
            await startResponse.Content.ReadFromJsonAsync<ManagedServiceSummary>();
        Assert.True(started.IsRunning);
    }
}
