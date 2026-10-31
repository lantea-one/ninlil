using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.AspNetCore.Mvc.Testing;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <c>/api/dhcp-services</c> of <see cref="DhcpServiceControlController" />
///     over HTTP on the shared in-process host, signed in as the bootstrap administrator. The test
///     host leaves every DHCP-family setting at its default, so DHCPv4, DHCPv6, Router
///     Advertisement and TFTP all start disabled and stopped.
///     <para>
///         Only DHCPv6 is started, and on a second host
///         (<see cref="NinlilDnsWebApplicationFactory.CreateSecondHost" />) whose DHCPv6 server
///         port is a free port the operating system chose, so the test never binds the standard
///         port 547 that a DHCP server on the machine may hold. DHCPv4 (67/68) and TFTP (69) are
///         not started here. Router Advertisement uses a raw socket that needs
///         <c>CAP_NET_RAW</c> rather than a port, so it is not started either. Starting and
///         stopping each service on real sockets is covered by <c>Dhcpv4LiveServiceControlTests</c>,
///         <c>Dhcpv6LiveServiceControlTests</c>, <c>TftpServerLiveServiceControlTests</c> and
///         <c>Icmpv6RaListenerLiveServiceControlTests</c>.
///     </para>
///     <para>
///         The DHCPv6 test checks the socket with a probe socket that binds the same
///         <c>IPv6Any</c> endpoint as <c>Ninlil.Dhcp.Protocol.Dhcpv6.Dhcpv6UdpListener.StartAsync</c>.
///         The probe does not set <c>SO_REUSEADDR</c>, and on Linux that option only helps a
///         socket that sets it, so the probe can bind only while no other socket holds the port.
///         This needs no DHCPv6 scope or SOLICIT exchange.
///     </para>
/// </summary>
[Collection(WebTestCollection.Name)]
public class DhcpServiceControlControllerTests(NinlilDnsWebApplicationFactory factory)
{
    private Task<HttpClient> CreateAuthenticatedClientAsync() =>
        CreateAuthenticatedClientAsync(factory);

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(
        WebApplicationFactory<Program> host
    )
    {
        HttpClient client = host.CreateClient();

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

    [Fact]
    public async Task List_WithAnAuthenticatedCaller_ReportsAllFourServices()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/dhcp-services");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<DhcpServiceStatus> services = await response.Content.ReadFromJsonAsync<
            List<DhcpServiceStatus>
        >();

        Assert.Equal(4, services.Count);
        Assert.Equal(services.Select(service => service.Kind).Distinct().Count(), services.Count);
        Assert.Contains(services, service => service.Kind == DhcpServiceKindEnum.Dhcpv4);
        Assert.Contains(services, service => service.Kind == DhcpServiceKindEnum.Dhcpv6);
        Assert.Contains(
            services,
            service => service.Kind == DhcpServiceKindEnum.RouterAdvertisement
        );
        Assert.Contains(services, service => service.Kind == DhcpServiceKindEnum.Tftp);
    }

    [Fact]
    public async Task DhcpServicesEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/dhcp-services");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Start_Stop_OnAnUnknownKind_BothReturnBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        const string unknownKind = "this-service-does-not-exist";

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsync($"/api/dhcp-services/{unknownKind}/start", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PostAsync($"/api/dhcp-services/{unknownKind}/stop", null)).StatusCode
        );
    }

    /// <summary>
    ///     This test checks that <see cref="DhcpServiceControlController.Stop" /> is a no-op for
    ///     DHCPv4, Router Advertisement and TFTP in their never-started state, which needs no
    ///     privilege. DHCPv6 is left out here; its stop is covered by the round-trip test.
    /// </summary>
    [Fact]
    public async Task StopIsAlwaysASafeNoOpEvenWhenNeverStarted()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        foreach (string kind in new[] { "dhcpv4", "routeradvertisement", "tftp" })
        {
            HttpResponseMessage response = await client.PostAsync(
                $"/api/dhcp-services/{kind}/stop",
                null
            );

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            DhcpServiceStatus status =
                await response.Content.ReadFromJsonAsync<DhcpServiceStatus>();
            Assert.False(status.IsRunning);
        }
    }

    /// <summary>
    ///     This test checks that starting DHCPv6 through the API binds its socket (a probe can no
    ///     longer bind the port) and stopping it releases the socket (the probe can bind again),
    ///     not just that <c>IsRunning</c> changes. It always leaves DHCPv6 stopped, since the
    ///     order of test classes is not fixed, as <see cref="ServicesControllerCrudTests" /> does.
    ///     It runs on a second host whose DHCPv6 server port is a free port.
    /// </summary>
    [Fact]
    public async Task Start_OnDhcpv6_GenuinelyBindsTheRealSocket_AndStopGenuinelyReleasesIt()
    {
        int serverPort = GetFreeUdpPort();
        using WebApplicationFactory<Program> host = factory.CreateSecondHost(builder =>
            builder.UseSetting(
                $"{Dhcpv6ProtocolOptions.SectionName}:{nameof(Dhcpv6ProtocolOptions.ServerPort)}",
                serverPort.ToString()
            )
        );
        using HttpClient client = await CreateAuthenticatedClientAsync(host);

        // Disabled at startup, not running, and the port is free.
        DhcpServiceStatus before = await GetStatusAsync(client, DhcpServiceKindEnum.Dhcpv6);
        Assert.False(before.Enabled);
        Assert.False(before.IsRunning);
        Assert.True(CanExclusivelyBindServerPort(serverPort));

        try
        {
            HttpResponseMessage startResponse = await client.PostAsync(
                "/api/dhcp-services/dhcpv6/start",
                null
            );
            Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
            DhcpServiceStatus started =
                await startResponse.Content.ReadFromJsonAsync<DhcpServiceStatus>();
            Assert.Equal(DhcpServiceKindEnum.Dhcpv6, started.Kind);
            Assert.True(started.IsRunning);

            // The socket is bound, so the probe can no longer bind the port.
            Assert.False(CanExclusivelyBindServerPort(serverPort));

            // A second Start while running must be a no-op, not a crash or a second bind.
            HttpResponseMessage secondStartResponse = await client.PostAsync(
                "/api/dhcp-services/dhcpv6/start",
                null
            );
            Assert.Equal(HttpStatusCode.OK, secondStartResponse.StatusCode);
            Assert.True(
                (await secondStartResponse.Content.ReadFromJsonAsync<DhcpServiceStatus>()).IsRunning
            );
        }
        finally
        {
            HttpResponseMessage stopResponse = await client.PostAsync(
                "/api/dhcp-services/dhcpv6/stop",
                null
            );
            Assert.Equal(HttpStatusCode.OK, stopResponse.StatusCode);
            DhcpServiceStatus stopped =
                await stopResponse.Content.ReadFromJsonAsync<DhcpServiceStatus>();
            Assert.False(stopped.IsRunning);
        }

        // The socket is released, so the probe can bind the port again.
        Assert.True(CanExclusivelyBindServerPort(serverPort));
    }

    /// <summary>
    ///     This method returns one service's status from <c>GET /api/dhcp-services</c>.
    /// </summary>
    private static async Task<DhcpServiceStatus> GetStatusAsync(
        HttpClient client,
        DhcpServiceKindEnum kind
    )
    {
        HttpResponseMessage response = await client.GetAsync("/api/dhcp-services");
        response.EnsureSuccessStatusCode();

        List<DhcpServiceStatus> services = await response.Content.ReadFromJsonAsync<
            List<DhcpServiceStatus>
        >();

        return Assert.Single(services, service => service.Kind == kind);
    }

    /// <summary>
    ///     This method reports whether a probe socket without <c>SO_REUSEADDR</c> can bind the
    ///     <c>IPv6Any</c> endpoint on <paramref name="serverPort" /> that
    ///     <c>Dhcpv6UdpListener.StartAsync</c> binds. It returns
    ///     <see langword="true" /> only when no other socket holds the port (see the class summary).
    /// </summary>
    private static bool CanExclusivelyBindServerPort(int serverPort)
    {
        try
        {
            using Socket probe = new(
                AddressFamily.InterNetworkV6,
                SocketType.Dgram,
                ProtocolType.Udp
            );
            probe.Bind(new IPEndPoint(IPAddress.IPv6Any, serverPort));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>
    ///     This method asks the operating system for a currently unused UDP port on the same
    ///     dual-stack wildcard endpoint the DHCPv6 listener binds, by binding port 0 and reading
    ///     back what was assigned.
    /// </summary>
    private static int GetFreeUdpPort()
    {
        using Socket probe = new(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
        return ((IPEndPoint)probe.LocalEndPoint).Port;
    }
}
