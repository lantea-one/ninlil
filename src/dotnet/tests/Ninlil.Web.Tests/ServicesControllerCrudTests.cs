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
///     This class covers <c>/api/services</c> of <see cref="ServicesController" /> on the shared
///     in-process host, signed in as the bootstrap administrator. It lists the registered
///     services, and checks that start, stop and restart change what the host does, not just the
///     reported status: stopping the Plain DNS UDP listener makes a UDP query to
///     <see cref="NinlilDnsWebApplicationFactory.PlainDnsPort" /> go unanswered, and starting it
///     again makes the same query answer, as in <see cref="DnsWiringTests" />.
///     <para>
///         Test classes in the collection never run concurrently (see
///         <see cref="WebTestCollection" />), but their order is not fixed, and others, such as
///         <see cref="DnsWiringTests" />, need the Plain DNS listeners up. Every test that stops a
///         service therefore starts it again before returning. Starting a service that is already
///         running is not tested.
///     </para>
/// </summary>
[Collection(WebTestCollection.Name)]
public class ServicesControllerCrudTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This constant contains the number of services <see cref="Services.ManagedServiceRegistry.Build" />
    ///     registers: 7 DNS transport listeners (Plain UDP and TCP, DNS-over-TLS, DNS-over-QUIC,
    ///     DNSCrypt UDP and TCP, DNS-over-HTTPS) and 19 background loops. Every service is
    ///     registered whether or not its feature is on; <c>filter-list-external-refresh</c>, for
    ///     example, is listed with filtering off, so a live <c>FilteringSettingsController.Patch</c>
    ///     can turn filtering on without a restart. The count is asserted so a change to the
    ///     registry is noticed here.
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
    ///     This method sends one UDP query to the host's Plain DNS listener and reports whether a
    ///     matching answer came back within two seconds. It returns <see langword="false" /> when
    ///     the socket is unreachable (connection refused or reset) or nothing arrives in time,
    ///     either of which is what a stopped listener looks like.
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

        // Every name is distinct.
        Assert.Equal(
            services.Select(service => service.Name).Distinct(StringComparer.Ordinal).Count(),
            services.Count
        );

        // One transport and one background service, both running, since the host started them
        // before the request.
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
    ///     This test checks that stopping the Plain DNS UDP listener through the API unbinds its
    ///     socket, so a UDP query gets no answer, rather than only reporting <c>IsRunning: false</c>.
    ///     Starting it again rebinds the socket and answers return. The test always leaves the
    ///     listener running (see the class summary).
    /// </summary>
    [Fact]
    public async Task Stop_OnThePlainUdpTransport_ActuallyStopsAnsweringRealQueries_AndStartResumes()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // An answer must arrive before anything is changed.
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

            // The socket is unbound, so the query gets no answer.
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

        // The socket answers again.
        Assert.True(await TryRealUdpQueryAsync());
    }

    /// <summary>
    ///     This test covers the stop-then-start sequence of <see cref="ServicesController.Restart" />
    ///     on the Plain DNS TCP listener, kept apart from the UDP listener that
    ///     <see cref="Stop_OnThePlainUdpTransport_ActuallyStopsAnsweringRealQueries_AndStartResumes" />
    ///     uses so a failure in one does not affect the other. The socket being bound again is
    ///     read from <see cref="ManagedServiceSummary.IsRunning" />, which for a transport is the
    ///     listener's <c>IsListening</c>, the signal <c>TransportListenersHealthCheck</c> uses. No
    ///     TCP query is sent.
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
    ///     This test covers a background loop, which has no running signal of its own, so
    ///     <see cref="ManagedServiceSummary.IsRunning" /> comes from the start and stop bookkeeping
    ///     of <see cref="ManagedServiceRegistration" />. It checks that the bookkeeping changes in
    ///     both directions, not that the loop stops working.
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

    /// <summary>
    ///     This test starts a background loop that is already running. The call answers 200 with
    ///     the state still running; that no second loop starts is proved by
    ///     <c>ManagedServiceRegistrationTests</c>.
    /// </summary>
    [Fact]
    public async Task Start_OnABackgroundServiceThatIsAlreadyRunning_ReturnsItRunning()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsync(
            "/api/services/query-log-cleanup/start",
            null
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<ManagedServiceSummary>()).IsRunning);

        response = await client.PostAsync("/api/services/query-log-cleanup/start", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<ManagedServiceSummary>()).IsRunning);
    }
}
