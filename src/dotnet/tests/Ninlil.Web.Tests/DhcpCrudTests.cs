using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the DHCP CRUD REST endpoints end to end, against a real, in-process host
///     and a real SQLite database (see <see cref="NinlilDnsWebApplicationFactory" />) -- full
///     lifecycles for <see cref="DhcpScopesController" />, <see cref="DhcpReservationsController" />,
///     <see cref="DhcpScopeCustomOptionsController" />, <see cref="DhcpTagRulesController" />, and
///     <see cref="DhcpTagRuleOptionsController" />, plus this feature's own deliberate design
///     choices: a scope's own address range must match its declared protocol version, a
///     reservation is unique per scope by both client identifier and IP address, and deleting a
///     scope/tag rule cascades to its own dependent rows.
/// </summary>
[Collection(WebTestCollection.Name)]
public class DhcpCrudTests(NinlilDnsWebApplicationFactory factory)
{
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

    private static HttpRequestMessage PatchRequest(string url, object body) =>
        new(HttpMethod.Patch, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"
            ),
        };

    private static async Task<DhcpScopeDetail> CreateScopeAsync(HttpClient client, string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name,
                protocolVersion = "IPv4",
                rangeStartAddress = "192.168.50.100",
                rangeEndAddress = "192.168.50.200",
                subnetPrefixLength = 24,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
                gatewayAddresses = new[] { "192.168.50.1" },
                dnsServerAddresses = new[] { "192.168.50.1" },
            }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<DhcpScopeDetail>();
    }

    [Fact]
    public async Task ScopeLifecycle_CreateEditReservationsCustomOptionsAndDelete_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string name = $"scope-{Guid.NewGuid():N}";

        DhcpScopeDetail created = await CreateScopeAsync(client, name);
        Assert.Equal(name, created.Name);
        Assert.Equal(24, created.SubnetPrefixLength);

        // Duplicate name must be rejected.
        HttpResponseMessage duplicateResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name,
                protocolVersion = "IPv4",
                rangeStartAddress = "192.168.51.100",
                rangeEndAddress = "192.168.51.200",
                subnetPrefixLength = 24,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
            }
        );
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        // An IPv6 address inside an IPv4 scope's own range must be rejected.
        HttpResponseMessage badRangeResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name = $"bad-{Guid.NewGuid():N}",
                protocolVersion = "IPv4",
                rangeStartAddress = "::1",
                rangeEndAddress = "192.168.52.200",
                subnetPrefixLength = 24,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, badRangeResponse.StatusCode);

        // Listing reflects the new scope.
        HttpResponseMessage listResponse = await client.GetAsync("/api/dhcp-scopes");
        listResponse.EnsureSuccessStatusCode();
        PagedResult<DhcpScopeDetail> page = await listResponse.Content.ReadFromJsonAsync<
            PagedResult<DhcpScopeDetail>
        >();
        Assert.Contains(page.Items, scope => scope.Id == created.Id);

        // PATCH narrows the range and disables the scope.
        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest(
                $"/api/dhcp-scopes/{created.Id}",
                new { rangeStartAddress = "192.168.50.150", isDisabled = true }
            )
        );
        patchResponse.EnsureSuccessStatusCode();

        HttpResponseMessage afterPatchResponse = await client.GetAsync(
            $"/api/dhcp-scopes/{created.Id}"
        );
        DhcpScopeDetail afterPatch =
            await afterPatchResponse.Content.ReadFromJsonAsync<DhcpScopeDetail>();
        Assert.Equal("192.168.50.150", afterPatch.RangeStartAddress);
        Assert.True(afterPatch.IsDisabled);

        // Add a reservation.
        HttpResponseMessage reservationCreateResponse = await client.PostAsJsonAsync(
            $"/api/dhcp-scopes/{created.Id}/reservations",
            new
            {
                clientIdentifier = "aa:bb:cc:dd:ee:01",
                ipAddress = "192.168.50.101",
                hostName = "test-host",
            }
        );
        Assert.Equal(HttpStatusCode.Created, reservationCreateResponse.StatusCode);
        DhcpReservationDetail reservation =
            await reservationCreateResponse.Content.ReadFromJsonAsync<DhcpReservationDetail>();
        Assert.Equal("192.168.50.101", reservation.IpAddress);

        // A second reservation for the same client identifier in the same scope conflicts.
        HttpResponseMessage duplicateClientResponse = await client.PostAsJsonAsync(
            $"/api/dhcp-scopes/{created.Id}/reservations",
            new { clientIdentifier = "aa:bb:cc:dd:ee:01", ipAddress = "192.168.50.102" }
        );
        Assert.Equal(HttpStatusCode.Conflict, duplicateClientResponse.StatusCode);

        // A second reservation for the same address in the same scope also conflicts.
        HttpResponseMessage duplicateAddressResponse = await client.PostAsJsonAsync(
            $"/api/dhcp-scopes/{created.Id}/reservations",
            new { clientIdentifier = "aa:bb:cc:dd:ee:02", ipAddress = "192.168.50.101" }
        );
        Assert.Equal(HttpStatusCode.Conflict, duplicateAddressResponse.StatusCode);

        // Listing reservations reflects the one that succeeded, with pagination fields present.
        HttpResponseMessage reservationListResponse = await client.GetAsync(
            $"/api/dhcp-scopes/{created.Id}/reservations"
        );
        reservationListResponse.EnsureSuccessStatusCode();
        PagedResult<DhcpReservationDetail> reservationPage =
            await reservationListResponse.Content.ReadFromJsonAsync<
                PagedResult<DhcpReservationDetail>
            >();
        Assert.Single(reservationPage.Items);
        Assert.Equal(1, reservationPage.TotalCount);

        // Add a custom option.
        HttpResponseMessage customOptionCreateResponse = await client.PostAsJsonAsync(
            $"/api/dhcp-scopes/{created.Id}/custom-options",
            new { optionTag = 66, valueHex = Convert.ToHexString("10.0.0.5"u8.ToArray()) }
        );
        Assert.Equal(HttpStatusCode.Created, customOptionCreateResponse.StatusCode);
        DhcpScopeCustomOptionDetail customOption =
            await customOptionCreateResponse.Content.ReadFromJsonAsync<DhcpScopeCustomOptionDetail>();
        Assert.Equal(66, customOption.OptionTag);

        // An odd-length hex value is rejected.
        HttpResponseMessage badHexResponse = await client.PostAsJsonAsync(
            $"/api/dhcp-scopes/{created.Id}/custom-options",
            new { optionTag = 67, valueHex = "ABC" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, badHexResponse.StatusCode);

        // Deleting the scope cascades to its reservations and custom options.
        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/dhcp-scopes/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage getAfterDeleteResponse = await client.GetAsync(
            $"/api/dhcp-scopes/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, getAfterDeleteResponse.StatusCode);

        HttpResponseMessage reservationAfterDeleteResponse = await client.GetAsync(
            $"/api/dhcp-scopes/{created.Id}/reservations/{reservation.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, reservationAfterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task ScopePxeBootFields_ValidateOnWriteAndPersistThroughPatch()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string name = $"pxe-scope-{Guid.NewGuid():N}";

        // A boot file name longer than RFC 2131's own 128-octet "file" header field cap is
        // rejected outright, rather than only discovered later as a thrown exception the first
        // time a lease is actually granted from this scope.
        HttpResponseMessage tooLongResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name,
                protocolVersion = "IPv4",
                rangeStartAddress = "192.168.60.100",
                rangeEndAddress = "192.168.60.200",
                subnetPrefixLength = 24,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
                bootFileName = new string('a', 129),
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, tooLongResponse.StatusCode);

        // An invalid next-server address is also rejected outright.
        HttpResponseMessage badAddressResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name,
                protocolVersion = "IPv4",
                rangeStartAddress = "192.168.60.100",
                rangeEndAddress = "192.168.60.200",
                subnetPrefixLength = 24,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
                bootFileName = "pxelinux.0",
                nextServerAddress = "not-an-ip",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, badAddressResponse.StatusCode);

        DhcpScopeDetail created = await CreateScopeAsync(client, name);
        Assert.Null(created.BootFileName);
        Assert.Null(created.NextServerAddress);

        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest(
                $"/api/dhcp-scopes/{created.Id}",
                new { bootFileName = "pxelinux.0", nextServerAddress = "192.168.60.10" }
            )
        );
        patchResponse.EnsureSuccessStatusCode();

        HttpResponseMessage afterPatchResponse = await client.GetAsync(
            $"/api/dhcp-scopes/{created.Id}"
        );
        DhcpScopeDetail afterPatch =
            await afterPatchResponse.Content.ReadFromJsonAsync<DhcpScopeDetail>();
        Assert.Equal("pxelinux.0", afterPatch.BootFileName);
        Assert.Equal("192.168.60.10", afterPatch.NextServerAddress);

        // Clearing both fields back out via a null merge-patch value works too.
        HttpResponseMessage clearResponse = await client.SendAsync(
            PatchRequest(
                $"/api/dhcp-scopes/{created.Id}",
                new { bootFileName = (string)null, nextServerAddress = (string)null }
            )
        );
        clearResponse.EnsureSuccessStatusCode();

        HttpResponseMessage afterClearResponse = await client.GetAsync(
            $"/api/dhcp-scopes/{created.Id}"
        );
        DhcpScopeDetail afterClear =
            await afterClearResponse.Content.ReadFromJsonAsync<DhcpScopeDetail>();
        Assert.Null(afterClear.BootFileName);
        Assert.Null(afterClear.NextServerAddress);

        await client.DeleteAsync($"/api/dhcp-scopes/{created.Id}");
    }

    [Fact]
    public async Task ScopeKindAndDelegatedPrefixLength_ValidateOnWriteAndPersistThroughPatch()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // A 'PrefixDelegation' scope is IPv6-only -- RFC 8415 Prefix Delegation has no DHCPv4
        // analogue at all.
        HttpResponseMessage ipv4PdResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name = $"bad-pd-ipv4-{Guid.NewGuid():N}",
                protocolVersion = "IPv4",
                kind = "PrefixDelegation",
                delegatedPrefixLength = 60,
                rangeStartAddress = "192.168.70.100",
                rangeEndAddress = "192.168.70.200",
                subnetPrefixLength = 24,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, ipv4PdResponse.StatusCode);

        // A 'PrefixDelegation' scope requires a real, in-range delegated prefix length.
        HttpResponseMessage missingLengthResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name = $"bad-pd-nolength-{Guid.NewGuid():N}",
                protocolVersion = "IPv6",
                kind = "PrefixDelegation",
                rangeStartAddress = "2001:db8:1000::",
                rangeEndAddress = "2001:db8:1fff:ffff:ffff:ffff:ffff:ffff",
                subnetPrefixLength = 48,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, missingLengthResponse.StatusCode);

        // An 'AddressAssignment' scope (the default) must not carry a delegated prefix length.
        HttpResponseMessage strayLengthResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name = $"bad-aa-stray-length-{Guid.NewGuid():N}",
                protocolVersion = "IPv6",
                delegatedPrefixLength = 60,
                rangeStartAddress = "2001:db8:2000::",
                rangeEndAddress = "2001:db8:2000::ffff",
                subnetPrefixLength = 64,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, strayLengthResponse.StatusCode);

        // A well-formed IPv6 Prefix Delegation scope is accepted.
        string pdName = $"pd-scope-{Guid.NewGuid():N}";
        HttpResponseMessage pdCreateResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name = pdName,
                protocolVersion = "IPv6",
                kind = "PrefixDelegation",
                delegatedPrefixLength = 60,
                rangeStartAddress = "2001:db8:3000::",
                rangeEndAddress = "2001:db8:3fff:ffff:ffff:ffff:ffff:ffff",
                subnetPrefixLength = 48,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
            }
        );
        Assert.Equal(HttpStatusCode.Created, pdCreateResponse.StatusCode);
        DhcpScopeDetail pdScope =
            await pdCreateResponse.Content.ReadFromJsonAsync<DhcpScopeDetail>();
        Assert.Equal(DhcpScopeKindEnum.PrefixDelegation, pdScope.Kind);
        Assert.Equal(60, pdScope.DelegatedPrefixLength);

        // PATCH can narrow the delegated prefix length further.
        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest($"/api/dhcp-scopes/{pdScope.Id}", new { delegatedPrefixLength = 64 })
        );
        patchResponse.EnsureSuccessStatusCode();

        HttpResponseMessage afterPatchResponse = await client.GetAsync(
            $"/api/dhcp-scopes/{pdScope.Id}"
        );
        DhcpScopeDetail afterPatch =
            await afterPatchResponse.Content.ReadFromJsonAsync<DhcpScopeDetail>();
        Assert.Equal(64, afterPatch.DelegatedPrefixLength);

        // PATCHing the delegated prefix length out of range is rejected.
        HttpResponseMessage badPatchResponse = await client.SendAsync(
            PatchRequest($"/api/dhcp-scopes/{pdScope.Id}", new { delegatedPrefixLength = 200 })
        );
        Assert.Equal(HttpStatusCode.BadRequest, badPatchResponse.StatusCode);

        // An ordinary IPv4 scope created with no 'kind' at all defaults to 'AddressAssignment',
        // with no delegated prefix length.
        DhcpScopeDetail ordinaryScope = await CreateScopeAsync(
            client,
            $"ordinary-{Guid.NewGuid():N}"
        );
        Assert.Equal(DhcpScopeKindEnum.AddressAssignment, ordinaryScope.Kind);
        Assert.Null(ordinaryScope.DelegatedPrefixLength);

        await client.DeleteAsync($"/api/dhcp-scopes/{pdScope.Id}");
        await client.DeleteAsync($"/api/dhcp-scopes/{ordinaryScope.Id}");
    }

    [Fact]
    public async Task ReservationCreateAndReplace_AgainstAPrefixDelegationScope_AreRejected()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // A Prefix Delegation scope has no delegated-prefix reservation shape at all (a
        // deliberate, documented Phase 14j scope cut) -- a reservation write against one is
        // always rejected with a real ValidationException, not silently accepted as an ordinary
        // address reservation.
        HttpResponseMessage pdCreateResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name = $"pd-no-reservations-{Guid.NewGuid():N}",
                protocolVersion = "IPv6",
                kind = "PrefixDelegation",
                delegatedPrefixLength = 60,
                rangeStartAddress = "2001:db8:4000::",
                rangeEndAddress = "2001:db8:4fff:ffff:ffff:ffff:ffff:ffff",
                subnetPrefixLength = 48,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
            }
        );
        Assert.Equal(HttpStatusCode.Created, pdCreateResponse.StatusCode);
        DhcpScopeDetail pdScope =
            await pdCreateResponse.Content.ReadFromJsonAsync<DhcpScopeDetail>();

        HttpResponseMessage reservationCreateResponse = await client.PostAsJsonAsync(
            $"/api/dhcp-scopes/{pdScope.Id}/reservations",
            new
            {
                clientIdentifier = "00:01:00:01:2c:8b:9c:3d:aa:bb:cc:dd:ee:01",
                ipAddress = "2001:db8:4000::",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, reservationCreateResponse.StatusCode);

        await client.DeleteAsync($"/api/dhcp-scopes/{pdScope.Id}");
    }

    [Fact]
    public async Task TagRuleLifecycle_CreateEditOptionsAndDelete_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string name = $"rule-{Guid.NewGuid():N}";

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            "/api/dhcp-tag-rules",
            new
            {
                name,
                priority = 5,
                vendorClassIdentifierMatch = "PXEClient",
                vendorClassIdentifierMatchMode = "Prefix",
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        DhcpTagRuleDetail created =
            await createResponse.Content.ReadFromJsonAsync<DhcpTagRuleDetail>();
        Assert.Equal(name, created.Name);
        Assert.Null(created.ScopeId);

        // A scopeId that doesn't exist is rejected.
        HttpResponseMessage badScopeResponse = await client.PostAsJsonAsync(
            "/api/dhcp-tag-rules",
            new { name = $"bad-{Guid.NewGuid():N}", scopeId = Guid.NewGuid() }
        );
        Assert.Equal(HttpStatusCode.BadRequest, badScopeResponse.StatusCode);

        // PATCH raises the priority.
        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest($"/api/dhcp-tag-rules/{created.Id}", new { priority = 20 })
        );
        patchResponse.EnsureSuccessStatusCode();

        HttpResponseMessage afterPatchResponse = await client.GetAsync(
            $"/api/dhcp-tag-rules/{created.Id}"
        );
        DhcpTagRuleDetail afterPatch =
            await afterPatchResponse.Content.ReadFromJsonAsync<DhcpTagRuleDetail>();
        Assert.Equal(20, afterPatch.Priority);

        // Add an option to the rule.
        HttpResponseMessage optionCreateResponse = await client.PostAsJsonAsync(
            $"/api/dhcp-tag-rules/{created.Id}/options",
            new { optionTag = 67, valueHex = Convert.ToHexString("pxelinux.0"u8.ToArray()) }
        );
        Assert.Equal(HttpStatusCode.Created, optionCreateResponse.StatusCode);
        DhcpTagRuleOptionDetail option =
            await optionCreateResponse.Content.ReadFromJsonAsync<DhcpTagRuleOptionDetail>();
        Assert.Equal(67, option.OptionTag);

        HttpResponseMessage optionListResponse = await client.GetAsync(
            $"/api/dhcp-tag-rules/{created.Id}/options"
        );
        optionListResponse.EnsureSuccessStatusCode();
        PagedResult<DhcpTagRuleOptionDetail> optionPage =
            await optionListResponse.Content.ReadFromJsonAsync<
                PagedResult<DhcpTagRuleOptionDetail>
            >();
        Assert.Single(optionPage.Items);

        // Deleting the rule cascades to its own options.
        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/dhcp-tag-rules/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage optionAfterDeleteResponse = await client.GetAsync(
            $"/api/dhcp-tag-rules/{created.Id}/options/{option.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, optionAfterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task DhcpEndpoints_WithoutAToken_ReturnUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/dhcp-scopes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
