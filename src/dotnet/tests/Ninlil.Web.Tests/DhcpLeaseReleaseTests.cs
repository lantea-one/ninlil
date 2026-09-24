using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="DhcpLeasesController.Release" /> end to end, against a real,
///     in-process host and a real SQLite database -- proving the exact thing this action's own doc
///     comment promises: a lease genuinely granted by the real DHCPv4 allocation engine (a real
///     DISCOVER/REQUEST exchange driven directly through the real, DI-registered
///     <see cref="IDhcpv4RequestHandler" />, exactly as <c>Ninlil.Dhcp.Server.Core.Tests</c>'s own
///     <c>Dhcpv4RequestHandlerTests</c> does), force-released through this REST action, genuinely
///     becomes available for a real subsequent allocation by a different client -- not merely that
///     the underlying database row's own <c>State</c> column changed.
/// </summary>
[Collection(WebTestCollection.Name)]
public class DhcpLeaseReleaseTests(NinlilDnsWebApplicationFactory factory)
{
    private static readonly IPEndPoint RemoteEndPoint = new(IPAddress.Loopback, 68);

    /// <summary>
    ///     This test's own scope subnet's real first address, set as every test message's own
    ///     relay agent address (<c>giaddr</c>) -- this shared factory's own DHCPv4 engine (see
    ///     <c>Ninlil.Dhcp.Server.Core.Scope.Dhcpv4ScopeResolver</c>'s own doc comment) can resolve
    ///     an ordinary, non-relayed request to "the single enabled scope" only when this shared,
    ///     collection-wide host has exactly one DHCPv4 scope registered at all -- never
    ///     guaranteed true here, since many other test classes in this same collection each
    ///     register their own scopes against the identical shared host. Setting a real relay
    ///     address within this test's own scope's subnet makes scope resolution deterministic
    ///     (RFC 2131 Section 4.1's own <c>giaddr</c> rule) regardless of how many unrelated scopes
    ///     already exist.
    /// </summary>
    private static readonly IPAddress RelayAgentAddress = IPAddress.Parse("192.168.77.1");

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

    [Fact]
    public async Task Release_ForARealGrantedLease_MakesTheAddressGenuinelyAvailableAgain()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string scopeName = $"release-scope-{Guid.NewGuid():N}";

        // A deliberately single-address pool -- the only way a second client's own DISCOVER can
        // possibly succeed afterward is if this test's own force-release genuinely freed this
        // exact address back into the real allocation engine's own pool.
        HttpResponseMessage scopeResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name = scopeName,
                protocolVersion = "IPv4",
                rangeStartAddress = "192.168.77.50",
                rangeEndAddress = "192.168.77.50",
                subnetPrefixLength = 24,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
            }
        );
        scopeResponse.EnsureSuccessStatusCode();

        // Grant a real, active lease through the real, DI-registered DHCPv4 engine -- a genuine
        // DISCOVER -> REQUEST -> ACK exchange, not a directly-seeded row.
        IDhcpv4RequestHandler handler =
            factory.Services.GetRequiredService<IDhcpv4RequestHandler>();
        byte[] clientHardwareAddress = [0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0x50];

        Dhcpv4Message discover = NewMessage(clientHardwareAddress, 0x9000);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        discover.RelayAgentIpAddress = RelayAgentAddress;
        Dhcpv4Message offer = await handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(offer);

        Dhcpv4Message request = NewMessage(clientHardwareAddress, 0x9000);
        request.SetMessageType(Dhcpv4MessageTypeEnum.Request);
        request.RelayAgentIpAddress = RelayAgentAddress;
        request.SetRequestedIpAddress(offer.YourIpAddress);
        Dhcpv4Message ack = await handler.HandleRequestAsync(
            request,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(ack);
        Assert.True(ack.TryGetMessageType(out Dhcpv4MessageTypeEnum ackType));
        Assert.Equal(Dhcpv4MessageTypeEnum.Ack, ackType);

        LeaseEntity grantedLease;
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ILeaseRepository leaseRepository =
                scope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            grantedLease = await leaseRepository.FindByClientIdentifierAsync(
                scopeName,
                "aa:bb:cc:dd:ee:50"
            );
        }
        Assert.NotNull(grantedLease);
        Assert.Equal(LeaseStateEnum.Active, grantedLease.State);

        // A different client's own DISCOVER against this now-full, single-address scope gets no
        // offer at all -- the real engine's own genuine confirmation the address is currently
        // unavailable, established before this test force-releases it.
        byte[] otherClientHardwareAddress = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66];
        Dhcpv4Message discoverBeforeRelease = NewMessage(otherClientHardwareAddress, 0x9001);
        discoverBeforeRelease.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        discoverBeforeRelease.RelayAgentIpAddress = RelayAgentAddress;
        Dhcpv4Message offerBeforeRelease = await handler.HandleRequestAsync(
            discoverBeforeRelease,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.Null(offerBeforeRelease);

        // Force-release the lease through the new REST action.
        HttpResponseMessage releaseResponse = await client.PostAsync(
            $"/api/dhcp-leases/{grantedLease.Id}/release",
            content: null
        );
        releaseResponse.EnsureSuccessStatusCode();
        DhcpLeaseDetail released =
            await releaseResponse.Content.ReadFromJsonAsync<DhcpLeaseDetail>();
        Assert.Equal(LeaseStateEnum.Released, released.State);

        // The genuine proof: a second, different client's own DISCOVER now succeeds, offered the
        // exact same address -- the real allocation engine's own decision, not an assertion
        // against the database row directly.
        Dhcpv4Message discoverAfterRelease = NewMessage(otherClientHardwareAddress, 0x9002);
        discoverAfterRelease.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        discoverAfterRelease.RelayAgentIpAddress = RelayAgentAddress;
        Dhcpv4Message offerAfterRelease = await handler.HandleRequestAsync(
            discoverAfterRelease,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(offerAfterRelease);
        Assert.Equal(IPAddress.Parse("192.168.77.50"), offerAfterRelease.YourIpAddress);
    }

    [Fact]
    public async Task Release_ForAnAlreadyReleasedLease_IsAnIdempotentNoOp()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string scopeName = $"release-idempotent-scope-{Guid.NewGuid():N}";

        Guid leaseId = Guid.NewGuid();
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            ILeaseRepository leaseRepository =
                scope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = leaseId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = scopeName,
                    ClientIdentifier = "aa:bb:cc:dd:ee:99",
                    IpAddress = "192.168.78.10",
                    State = LeaseStateEnum.Released,
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                }
            );
            await unitOfWork.SaveChangesAsync();
        }

        HttpResponseMessage releaseResponse = await client.PostAsync(
            $"/api/dhcp-leases/{leaseId}/release",
            content: null
        );
        releaseResponse.EnsureSuccessStatusCode();
        DhcpLeaseDetail released =
            await releaseResponse.Content.ReadFromJsonAsync<DhcpLeaseDetail>();
        Assert.Equal(LeaseStateEnum.Released, released.State);
    }

    [Fact]
    public async Task Release_ForANonexistentLease_ReturnsNotFound()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage releaseResponse = await client.PostAsync(
            $"/api/dhcp-leases/{Guid.NewGuid()}/release",
            content: null
        );

        Assert.Equal(HttpStatusCode.NotFound, releaseResponse.StatusCode);
    }

    [Fact]
    public async Task Release_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage releaseResponse = await client.PostAsync(
            $"/api/dhcp-leases/{Guid.NewGuid()}/release",
            content: null
        );

        Assert.Equal(HttpStatusCode.Unauthorized, releaseResponse.StatusCode);
    }

    /// <summary>
    ///     This method builds the common message shape every test in this class shares -- mirrors
    ///     <c>Ninlil.Dhcp.Server.Core.Tests.Dhcpv4RequestHandlerTests</c>'s own helper exactly.
    /// </summary>
    private static Dhcpv4Message NewMessage(byte[] clientHardwareAddress, uint transactionId) =>
        new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = transactionId,
            HardwareAddressLength = (byte)clientHardwareAddress.Length,
            ClientHardwareAddress = clientHardwareAddress,
        };
}
