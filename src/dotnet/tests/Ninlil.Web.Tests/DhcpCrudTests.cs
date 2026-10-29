using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the DHCP configuration endpoints over HTTP on the shared in-process host,
///     signed in as the bootstrap administrator: the lifecycles of
///     <see cref="DhcpScopesController" />, <see cref="DhcpReservationsController" />,
///     <see cref="DhcpScopeCustomOptionsController" />, <see cref="DhcpTagRulesController" /> and
///     <see cref="DhcpTagRuleOptionsController" />, and the rules around them: a scope's range must
///     match its protocol version, PXE and prefix-delegation fields are validated, a reservation is
///     unique per scope by client identifier and by address, a tag rule's criteria must fit its
///     protocol (global and DHCPv4-scope rules are DHCPv4 rules, a DHCPv6 rule names a DHCPv6
///     scope), and deleting a scope or tag rule removes its reservations or options. No DHCP traffic is involved.
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

        // An IPv6 address in an IPv4 scope's range must be rejected.
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

        // Deleting the scope removes its reservations and custom options. The reservation GET
        // does not check the scope, so its 404 shows the row is gone; the custom option is not
        // checked.
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

        // A boot file name longer than the 128-octet "file" header field of RFC 2131 is rejected
        // on write, instead of failing later when a lease from this scope is first granted.
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

        // An invalid next-server address is rejected too.
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

        // A null merge-patch value clears both fields.
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

        // A 'PrefixDelegation' scope is IPv6-only; RFC 8415 Prefix Delegation has no DHCPv4
        // equivalent.
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

        // A 'PrefixDelegation' scope requires an in-range delegated prefix length.
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

        // An IPv4 scope created without 'kind' defaults to 'AddressAssignment', with no
        // delegated prefix length.
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

        // Prefix delegation scopes do not support reservations, so a reservation write to one is
        // rejected with a ValidationException rather than stored as an address reservation.
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

        // The API cannot create a reservation on such a scope, so the row that Replace finds is
        // stored through the repository.
        Guid reservationId = Guid.NewGuid();
        using (IServiceScope serviceScope = factory.Services.CreateScope())
        {
            await serviceScope
                .ServiceProvider.GetRequiredService<IDhcpReservationRepository>()
                .AddAsync(
                    new DhcpReservationEntity
                    {
                        Id = reservationId,
                        ScopeId = pdScope.Id,
                        ClientIdentifier = "00:01:00:01:2c:8b:9c:3d:aa:bb:cc:dd:ee:02",
                        IpAddress = "2001:db8:4000::1",
                    }
                );
            await serviceScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        HttpResponseMessage reservationReplaceResponse = await client.PutAsJsonAsync(
            $"/api/dhcp-scopes/{pdScope.Id}/reservations/{reservationId}",
            new
            {
                clientIdentifier = "00:01:00:01:2c:8b:9c:3d:aa:bb:cc:dd:ee:02",
                ipAddress = "2001:db8:4000::2",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, reservationReplaceResponse.StatusCode);

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

        // Deleting the rule removes its options.
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
    public async Task RawOptionRules_OnScopeCustomOptionsAndTagRuleOptions_RejectBadTagsAndLengths()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        DhcpScopeDetail scope = await CreateScopeAsync(client, $"raw-rules-{Guid.NewGuid():N}");
        string customOptionsUrl = $"/api/dhcp-scopes/{scope.Id}/custom-options";

        // Reserved DHCPv4 tags are rejected and the message names the tag.
        foreach (int tag in new[] { 0, 52, 53, 82, 255 })
        {
            HttpResponseMessage reservedResponse = await client.PostAsJsonAsync(
                customOptionsUrl,
                new { optionTag = tag, valueHex = "01" }
            );
            Assert.Equal(HttpStatusCode.BadRequest, reservedResponse.StatusCode);
            Assert.Contains($"tag {tag}", await reservedResponse.Content.ReadAsStringAsync());
        }

        // 256 bytes is one over the one-byte length field; 254 bytes is accepted.
        HttpResponseMessage tooLongResponse = await client.PostAsJsonAsync(
            customOptionsUrl,
            new { optionTag = 200, valueHex = new string('A', 512) }
        );
        Assert.Equal(HttpStatusCode.BadRequest, tooLongResponse.StatusCode);
        Assert.Contains("255", await tooLongResponse.Content.ReadAsStringAsync());

        HttpResponseMessage okResponse = await client.PostAsJsonAsync(
            customOptionsUrl,
            new { optionTag = 200, valueHex = new string('A', 508) }
        );
        Assert.Equal(HttpStatusCode.Created, okResponse.StatusCode);
        DhcpScopeCustomOptionDetail ok =
            await okResponse.Content.ReadFromJsonAsync<DhcpScopeCustomOptionDetail>();

        // The same rules apply to a replace.
        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"{customOptionsUrl}/{ok.Id}",
            new { optionTag = 53, valueHex = "01" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, replaceResponse.StatusCode);

        // A global tag rule feeds the DHCPv4 engine, so it gets the DHCPv4 limits.
        HttpResponseMessage globalRuleResponse = await client.PostAsJsonAsync(
            "/api/dhcp-tag-rules",
            new { name = $"raw-global-{Guid.NewGuid():N}" }
        );
        Assert.Equal(HttpStatusCode.Created, globalRuleResponse.StatusCode);
        DhcpTagRuleDetail globalRule =
            await globalRuleResponse.Content.ReadFromJsonAsync<DhcpTagRuleDetail>();

        HttpResponseMessage globalTooLongResponse = await client.PostAsJsonAsync(
            $"/api/dhcp-tag-rules/{globalRule.Id}/options",
            new { optionTag = 200, valueHex = new string('A', 512) }
        );
        Assert.Equal(HttpStatusCode.BadRequest, globalTooLongResponse.StatusCode);

        HttpResponseMessage globalReservedResponse = await client.PostAsJsonAsync(
            $"/api/dhcp-tag-rules/{globalRule.Id}/options",
            new { optionTag = 82, valueHex = "01" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, globalReservedResponse.StatusCode);

        // A rule for a DHCPv6 scope only has the DHCPv6 limits, so 300 bytes pass.
        HttpResponseMessage v6ScopeResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name = $"raw-rules-v6-{Guid.NewGuid():N}",
                protocolVersion = "IPv6",
                rangeStartAddress = "2001:db8:b6::100",
                rangeEndAddress = "2001:db8:b6::200",
                subnetPrefixLength = 64,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
            }
        );
        Assert.Equal(HttpStatusCode.Created, v6ScopeResponse.StatusCode);
        DhcpScopeDetail v6Scope =
            await v6ScopeResponse.Content.ReadFromJsonAsync<DhcpScopeDetail>();

        HttpResponseMessage v6RuleResponse = await client.PostAsJsonAsync(
            "/api/dhcp-tag-rules",
            new { name = $"raw-v6-{Guid.NewGuid():N}", scopeId = v6Scope.Id }
        );
        Assert.Equal(HttpStatusCode.Created, v6RuleResponse.StatusCode);
        DhcpTagRuleDetail v6Rule =
            await v6RuleResponse.Content.ReadFromJsonAsync<DhcpTagRuleDetail>();

        HttpResponseMessage v6LongResponse = await client.PostAsJsonAsync(
            $"/api/dhcp-tag-rules/{v6Rule.Id}/options",
            new { optionTag = 200, valueHex = new string('A', 600) }
        );
        Assert.Equal(HttpStatusCode.Created, v6LongResponse.StatusCode);

        HttpResponseMessage v6ZeroResponse = await client.PostAsJsonAsync(
            $"/api/dhcp-tag-rules/{v6Rule.Id}/options",
            new { optionTag = 0, valueHex = "01" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, v6ZeroResponse.StatusCode);
    }

    /// <summary>
    ///     This method creates a DHCPv6 scope with no gateway and returns it, for the tests of the
    ///     DHCPv6 custom option and address family rules.
    /// </summary>
    private static async Task<DhcpScopeDetail> CreateV6ScopeAsync(
        HttpClient client,
        string name,
        string prefix
    )
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name,
                protocolVersion = "IPv6",
                rangeStartAddress = $"{prefix}::100",
                rangeEndAddress = $"{prefix}::200",
                subnetPrefixLength = 64,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
                dnsServerAddresses = new[] { $"{prefix}::1" },
            }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<DhcpScopeDetail>();
    }

    [Fact]
    public async Task CustomOptions_OnADhcpv6Scope_GetTheDhcpv6Rules()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        DhcpScopeDetail scope = await CreateV6ScopeAsync(
            client,
            $"v6-custom-{Guid.NewGuid():N}",
            "2001:db8:b192"
        );
        string url = $"/api/dhcp-scopes/{scope.Id}/custom-options";

        // Codes the engine writes itself are refused; the message names the code.
        foreach (int code in new[] { 1, 2, 13, 39 })
        {
            HttpResponseMessage reservedResponse = await client.PostAsJsonAsync(
                url,
                new { optionTag = code, valueHex = "00" }
            );
            Assert.Equal(HttpStatusCode.BadRequest, reservedResponse.StatusCode);
            Assert.Contains($"code {code}", await reservedResponse.Content.ReadAsStringAsync());
        }

        // 70 000 bytes exceed the two-byte DHCPv6 option length.
        HttpResponseMessage tooLongResponse = await client.PostAsJsonAsync(
            url,
            new { optionTag = 32, valueHex = new string('A', 140_000) }
        );
        Assert.Equal(HttpStatusCode.BadRequest, tooLongResponse.StatusCode);

        // A text search list is not wire format; the wire form is accepted.
        HttpResponseMessage textListResponse = await client.PostAsJsonAsync(
            url,
            new { optionTag = 24, valueHex = "6578616D706C652E636F6D" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, textListResponse.StatusCode);

        HttpResponseMessage wireListResponse = await client.PostAsJsonAsync(
            url,
            new { optionTag = 24, valueHex = "076578616D706C6503636F6D00" }
        );
        Assert.Equal(HttpStatusCode.Created, wireListResponse.StatusCode);

        // Information Refresh Time (code 32) is a normal custom option, and a server address
        // needs no stored value on code 23.
        HttpResponseMessage refreshResponse = await client.PostAsJsonAsync(
            url,
            new { optionTag = 32, valueHex = "00000E10" }
        );
        Assert.Equal(HttpStatusCode.Created, refreshResponse.StatusCode);

        HttpResponseMessage serverAddressResponse = await client.PostAsJsonAsync(
            url,
            new { optionTag = 23, valueSource = "ServerAddress" }
        );
        Assert.Equal(HttpStatusCode.Created, serverAddressResponse.StatusCode);

        // The same rules apply to a replace.
        DhcpScopeCustomOptionDetail refresh =
            await refreshResponse.Content.ReadFromJsonAsync<DhcpScopeCustomOptionDetail>();
        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"{url}/{refresh.Id}",
            new { optionTag = 2, valueHex = "00" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, replaceResponse.StatusCode);
    }

    [Fact]
    public async Task ScopeWrites_WithAddressesOfTheWrongFamily_AreRejected()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        object V4Body(string gateway, string dns) =>
            new
            {
                name = $"family-v4-{Guid.NewGuid():N}",
                protocolVersion = "IPv4",
                rangeStartAddress = "192.168.60.100",
                rangeEndAddress = "192.168.60.200",
                subnetPrefixLength = 24,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
                gatewayAddresses = new[] { gateway },
                dnsServerAddresses = new[] { dns },
            };

        // An IPv6 gateway or DNS server on a DHCPv4 scope.
        HttpResponseMessage v4GatewayResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            V4Body("2001:db8::1", "192.168.60.1")
        );
        Assert.Equal(HttpStatusCode.BadRequest, v4GatewayResponse.StatusCode);
        Assert.Contains("2001:db8::1", await v4GatewayResponse.Content.ReadAsStringAsync());

        HttpResponseMessage v4DnsResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            V4Body("192.168.60.1", "2001:db8::53")
        );
        Assert.Equal(HttpStatusCode.BadRequest, v4DnsResponse.StatusCode);

        object V6Body(string[] gateways, string[] dns) =>
            new
            {
                name = $"family-v6-{Guid.NewGuid():N}",
                protocolVersion = "IPv6",
                rangeStartAddress = "2001:db8:b193::100",
                rangeEndAddress = "2001:db8:b193::200",
                subnetPrefixLength = 64,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
                gatewayAddresses = gateways,
                dnsServerAddresses = dns,
            };

        // An IPv4 DNS server on a DHCPv6 scope, and any gateway at all.
        HttpResponseMessage v6DnsResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            V6Body([], ["192.0.2.53"])
        );
        Assert.Equal(HttpStatusCode.BadRequest, v6DnsResponse.StatusCode);

        HttpResponseMessage v6GatewayResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            V6Body(["2001:db8::1"], ["2001:db8::53"])
        );
        Assert.Equal(HttpStatusCode.BadRequest, v6GatewayResponse.StatusCode);

        // A correct pair is accepted.
        HttpResponseMessage okResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            V6Body([], ["2001:db8::53"])
        );
        Assert.Equal(HttpStatusCode.Created, okResponse.StatusCode);
        DhcpScopeDetail ok = await okResponse.Content.ReadFromJsonAsync<DhcpScopeDetail>();

        // PUT checks the same rule.
        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/dhcp-scopes/{ok.Id}",
            new
            {
                name = ok.Name,
                protocolVersion = "IPv6",
                rangeStartAddress = "2001:db8:b193::100",
                rangeEndAddress = "2001:db8:b193::200",
                subnetPrefixLength = 64,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
                dnsServerAddresses = new[] { "192.0.2.53" },
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, replaceResponse.StatusCode);

        // PATCH checks the patched lists against the scope's protocol.
        HttpResponseMessage patchDnsResponse = await client.SendAsync(
            PatchRequest(
                $"/api/dhcp-scopes/{ok.Id}",
                new { dnsServerAddresses = new[] { "192.0.2.53" } }
            )
        );
        Assert.Equal(HttpStatusCode.BadRequest, patchDnsResponse.StatusCode);

        HttpResponseMessage patchGatewayResponse = await client.SendAsync(
            PatchRequest(
                $"/api/dhcp-scopes/{ok.Id}",
                new { gatewayAddresses = new[] { "2001:db8::1" } }
            )
        );
        Assert.Equal(HttpStatusCode.BadRequest, patchGatewayResponse.StatusCode);

        HttpResponseMessage patchOkResponse = await client.SendAsync(
            PatchRequest(
                $"/api/dhcp-scopes/{ok.Id}",
                new { dnsServerAddresses = new[] { "2001:db8::54" } }
            )
        );
        Assert.Equal(HttpStatusCode.NoContent, patchOkResponse.StatusCode);
    }

    [Fact]
    public async Task RawOptionValues_OnTftpServerNameAndDomainSearchList_AreCheckedByType()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        DhcpScopeDetail scope = await CreateScopeAsync(client, $"raw-values-{Guid.NewGuid():N}");
        string customOptionsUrl = $"/api/dhcp-scopes/{scope.Id}/custom-options";

        // C0000201 is the 4 binary octets of 192.0.2.1; option 66 is text (RFC 2132 Section 9.4).
        HttpResponseMessage binaryResponse = await client.PostAsJsonAsync(
            customOptionsUrl,
            new { optionTag = 66, valueHex = "C0000201" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, binaryResponse.StatusCode);
        Assert.Contains("printable ASCII", await binaryResponse.Content.ReadAsStringAsync());

        // The hex of the text "192.168.1.10".
        HttpResponseMessage textResponse = await client.PostAsJsonAsync(
            customOptionsUrl,
            new { optionTag = 66, valueHex = "3139322E3136382E312E3130" }
        );
        Assert.Equal(HttpStatusCode.Created, textResponse.StatusCode);

        // "This server's address" needs no stored value, so the value rule is not applied to it.
        // Tag 66 is taken by the row above (one option per tag and scope), so use tag 150.
        HttpResponseMessage serverAddressResponse = await client.PostAsJsonAsync(
            customOptionsUrl,
            new { optionTag = 150, valueSource = "ServerAddress" }
        );
        Assert.Equal(HttpStatusCode.Created, serverAddressResponse.StatusCode);

        HttpResponseMessage v6ScopeResponse = await client.PostAsJsonAsync(
            "/api/dhcp-scopes",
            new
            {
                name = $"raw-values-v6-{Guid.NewGuid():N}",
                protocolVersion = "IPv6",
                rangeStartAddress = "2001:db8:b19::100",
                rangeEndAddress = "2001:db8:b19::200",
                subnetPrefixLength = 64,
                defaultLeaseDuration = "1.00:00:00",
                maxLeaseDuration = "7.00:00:00",
            }
        );
        Assert.Equal(HttpStatusCode.Created, v6ScopeResponse.StatusCode);
        DhcpScopeDetail v6Scope =
            await v6ScopeResponse.Content.ReadFromJsonAsync<DhcpScopeDetail>();

        HttpResponseMessage v6RuleResponse = await client.PostAsJsonAsync(
            "/api/dhcp-tag-rules",
            new { name = $"raw-values-v6-{Guid.NewGuid():N}", scopeId = v6Scope.Id }
        );
        Assert.Equal(HttpStatusCode.Created, v6RuleResponse.StatusCode);
        DhcpTagRuleDetail v6Rule =
            await v6RuleResponse.Content.ReadFromJsonAsync<DhcpTagRuleDetail>();
        string optionsUrl = $"/api/dhcp-tag-rules/{v6Rule.Id}/options";

        // The hex of the text "example.com" is not a wire-format name list.
        HttpResponseMessage textListResponse = await client.PostAsJsonAsync(
            optionsUrl,
            new { optionTag = 24, valueHex = "6578616D706C652E636F6D" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, textListResponse.StatusCode);

        // 07example03com00.
        HttpResponseMessage wireListResponse = await client.PostAsJsonAsync(
            optionsUrl,
            new { optionTag = 24, valueHex = "076578616D706C6503636F6D00" }
        );
        Assert.Equal(HttpStatusCode.Created, wireListResponse.StatusCode);

        // Code 2 is the Server Identifier, which the engine writes itself.
        HttpResponseMessage serverIdResponse = await client.PostAsJsonAsync(
            optionsUrl,
            new { optionTag = 2, valueHex = "00030001AABBCCDDEEFF" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, serverIdResponse.StatusCode);
        Assert.Contains("Server Identifier", await serverIdResponse.Content.ReadAsStringAsync());

        // Preference stays allowed.
        HttpResponseMessage preferenceResponse = await client.PostAsJsonAsync(
            optionsUrl,
            new { optionTag = 7, valueHex = "05" }
        );
        Assert.Equal(HttpStatusCode.Created, preferenceResponse.StatusCode);
    }

    [Fact]
    public async Task TagRuleCriteria_ThatDoNotFitTheRulesProtocol_AreRejected()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        DhcpScopeDetail v6Scope = await CreateV6ScopeAsync(
            client,
            $"rule-proto-v6-{Guid.NewGuid():N}",
            "2001:db8:b193"
        );
        DhcpScopeDetail v4Scope = await CreateScopeAsync(
            client,
            $"rule-proto-v4-{Guid.NewGuid():N}"
        );

        // A global rule is a DHCPv4 rule: the DHCPv6 criteria are refused.
        HttpResponseMessage globalInterfaceId = await client.PostAsJsonAsync(
            "/api/dhcp-tag-rules",
            new { name = $"g-if-{Guid.NewGuid():N}", relayInterfaceIdHex = "0A0B" }
        );
        Assert.Equal(HttpStatusCode.BadRequest, globalInterfaceId.StatusCode);
        Assert.Contains("relayInterfaceIdHex", await globalInterfaceId.Content.ReadAsStringAsync());

        HttpResponseMessage globalEnterprise = await client.PostAsJsonAsync(
            "/api/dhcp-tag-rules",
            new { name = $"g-ent-{Guid.NewGuid():N}", vendorClassEnterpriseNumber = 32473 }
        );
        Assert.Equal(HttpStatusCode.BadRequest, globalEnterprise.StatusCode);

        // A rule on a DHCPv6 scope cannot use the DHCPv4 Circuit ID.
        HttpResponseMessage v6Circuit = await client.PostAsJsonAsync(
            "/api/dhcp-tag-rules",
            new
            {
                name = $"v6-circuit-{Guid.NewGuid():N}",
                scopeId = v6Scope.Id,
                relayAgentCircuitIdHex = "0102",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, v6Circuit.StatusCode);
        Assert.Contains("relayAgentCircuitIdHex", await v6Circuit.Content.ReadAsStringAsync());

        // A rule on a DHCPv4 scope may use the Circuit ID (guard) but not the Interface-ID.
        HttpResponseMessage v4Circuit = await client.PostAsJsonAsync(
            "/api/dhcp-tag-rules",
            new
            {
                name = $"v4-circuit-{Guid.NewGuid():N}",
                scopeId = v4Scope.Id,
                relayAgentCircuitIdHex = "0102",
            }
        );
        Assert.Equal(HttpStatusCode.Created, v4Circuit.StatusCode);

        HttpResponseMessage v4InterfaceId = await client.PostAsJsonAsync(
            "/api/dhcp-tag-rules",
            new
            {
                name = $"v4-if-{Guid.NewGuid():N}",
                scopeId = v4Scope.Id,
                relayInterfaceIdHex = "0A0B",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, v4InterfaceId.StatusCode);

        // A DHCPv6-scope rule with an Interface-ID is valid (guard); PUT moving it to a global
        // rule keeps the Interface-ID and is refused.
        HttpResponseMessage v6InterfaceId = await client.PostAsJsonAsync(
            "/api/dhcp-tag-rules",
            new
            {
                name = $"v6-if-{Guid.NewGuid():N}",
                scopeId = v6Scope.Id,
                relayInterfaceIdHex = "0A0B",
                vendorClassEnterpriseNumber = 32473,
            }
        );
        Assert.Equal(HttpStatusCode.Created, v6InterfaceId.StatusCode);
        DhcpTagRuleDetail v6Rule =
            await v6InterfaceId.Content.ReadFromJsonAsync<DhcpTagRuleDetail>();

        HttpResponseMessage putGlobal = await client.PutAsJsonAsync(
            $"/api/dhcp-tag-rules/{v6Rule.Id}",
            new
            {
                name = v6Rule.Name,
                scopeId = (Guid?)null,
                relayInterfaceIdHex = "0A0B",
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, putGlobal.StatusCode);

        // PATCH validates the merged result: moving the rule to scopeId null while it keeps the
        // DHCPv6-only criteria is refused and leaves the rule unchanged.
        HttpResponseMessage patchGlobal = await client.SendAsync(
            PatchRequest($"/api/dhcp-tag-rules/{v6Rule.Id}", new { scopeId = (Guid?)null })
        );
        Assert.Equal(HttpStatusCode.BadRequest, patchGlobal.StatusCode);
        DhcpTagRuleDetail unchanged = await (
            await client.GetAsync($"/api/dhcp-tag-rules/{v6Rule.Id}")
        ).Content.ReadFromJsonAsync<DhcpTagRuleDetail>();
        Assert.Equal(v6Scope.Id, unchanged.ScopeId);

        // Clearing the criteria in the same patch makes the move valid.
        HttpResponseMessage patchClear = await client.SendAsync(
            PatchRequest(
                $"/api/dhcp-tag-rules/{v6Rule.Id}",
                new
                {
                    scopeId = (Guid?)null,
                    relayInterfaceIdHex = (string)null,
                    vendorClassEnterpriseNumber = (uint?)null,
                }
            )
        );
        Assert.Equal(HttpStatusCode.NoContent, patchClear.StatusCode);
    }

    /// <summary>
    ///     This test posts the same option tag twice to one scope. The second post must answer 409
    ///     Conflict (the rule is checked before the save, so the database index never throws) and
    ///     the scope must keep one option with that tag.
    /// </summary>
    [Fact]
    public async Task ScopeCustomOptionCreate_WithATagTheScopeAlreadyHas_ReturnsConflict()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        DhcpScopeDetail scope = await CreateScopeAsync(client, $"dup-create-{Guid.NewGuid():N}");
        string url = $"/api/dhcp-scopes/{scope.Id}/custom-options";

        HttpResponseMessage first = await client.PostAsJsonAsync(
            url,
            new { optionTag = 200, valueHex = "0A" }
        );
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        HttpResponseMessage second = await client.PostAsJsonAsync(
            url,
            new { optionTag = 200, valueHex = "0B" }
        );
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        PagedResult<DhcpScopeCustomOptionDetail> page = await client.GetFromJsonAsync<
            PagedResult<DhcpScopeCustomOptionDetail>
        >(url);
        Assert.Single(page.Items);

        await client.DeleteAsync($"/api/dhcp-scopes/{scope.Id}");
    }

    /// <summary>
    ///     This test moves a scope's option onto the tag of another option of the same scope with
    ///     PUT (409), then replaces it with its own tag and a new value (204), because the row
    ///     being replaced does not conflict with itself.
    /// </summary>
    [Fact]
    public async Task ScopeCustomOptionReplace_OntoAnotherOptionsTag_ReturnsConflictButItsOwnTagIsAllowed()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        DhcpScopeDetail scope = await CreateScopeAsync(client, $"dup-replace-{Guid.NewGuid():N}");
        string url = $"/api/dhcp-scopes/{scope.Id}/custom-options";

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            url,
            new { optionTag = 200, valueHex = "0A" }
        );
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        HttpResponseMessage secondResponse = await client.PostAsJsonAsync(
            url,
            new { optionTag = 201, valueHex = "0B" }
        );
        DhcpScopeCustomOptionDetail second =
            await secondResponse.Content.ReadFromJsonAsync<DhcpScopeCustomOptionDetail>();

        HttpResponseMessage ontoTheFirstTag = await client.PutAsJsonAsync(
            $"{url}/{second.Id}",
            new { optionTag = 200, valueHex = "0B" }
        );
        Assert.Equal(HttpStatusCode.Conflict, ontoTheFirstTag.StatusCode);

        HttpResponseMessage ownTag = await client.PutAsJsonAsync(
            $"{url}/{second.Id}",
            new { optionTag = 201, valueHex = "0C" }
        );
        Assert.Equal(HttpStatusCode.NoContent, ownTag.StatusCode);

        await client.DeleteAsync($"/api/dhcp-scopes/{scope.Id}");
    }

    /// <summary>
    ///     This test posts the same option tag twice to one tag rule. The second post must answer
    ///     409 Conflict and the rule must keep one option with that tag.
    /// </summary>
    [Fact]
    public async Task TagRuleOptionCreate_WithATagTheRuleAlreadyHas_ReturnsConflict()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        DhcpTagRuleDetail rule = await CreateGlobalTagRuleAsync(client);
        string url = $"/api/dhcp-tag-rules/{rule.Id}/options";

        HttpResponseMessage first = await client.PostAsJsonAsync(
            url,
            new { optionTag = 201, valueHex = "0A" }
        );
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        HttpResponseMessage second = await client.PostAsJsonAsync(
            url,
            new { optionTag = 201, valueHex = "0B" }
        );
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        PagedResult<DhcpTagRuleOptionDetail> page = await client.GetFromJsonAsync<
            PagedResult<DhcpTagRuleOptionDetail>
        >(url);
        Assert.Single(page.Items);

        await client.DeleteAsync($"/api/dhcp-tag-rules/{rule.Id}");
    }

    /// <summary>
    ///     This test moves a tag rule's option onto the tag of another option of the same rule
    ///     with PUT (409), then replaces it with its own tag and a new value (204), because the
    ///     row being replaced does not conflict with itself.
    /// </summary>
    [Fact]
    public async Task TagRuleOptionReplace_OntoAnotherOptionsTag_ReturnsConflictButItsOwnTagIsAllowed()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        DhcpTagRuleDetail rule = await CreateGlobalTagRuleAsync(client);
        string url = $"/api/dhcp-tag-rules/{rule.Id}/options";

        HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            url,
            new { optionTag = 200, valueHex = "0A" }
        );
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        HttpResponseMessage secondResponse = await client.PostAsJsonAsync(
            url,
            new { optionTag = 201, valueHex = "0B" }
        );
        DhcpTagRuleOptionDetail second =
            await secondResponse.Content.ReadFromJsonAsync<DhcpTagRuleOptionDetail>();

        HttpResponseMessage ontoTheFirstTag = await client.PutAsJsonAsync(
            $"{url}/{second.Id}",
            new { optionTag = 200, valueHex = "0B" }
        );
        Assert.Equal(HttpStatusCode.Conflict, ontoTheFirstTag.StatusCode);

        HttpResponseMessage ownTag = await client.PutAsJsonAsync(
            $"{url}/{second.Id}",
            new { optionTag = 201, valueHex = "0C" }
        );
        Assert.Equal(HttpStatusCode.NoContent, ownTag.StatusCode);

        await client.DeleteAsync($"/api/dhcp-tag-rules/{rule.Id}");
    }

    /// <summary>This method creates a global (DHCPv4) tag rule with a unique name.</summary>
    private static async Task<DhcpTagRuleDetail> CreateGlobalTagRuleAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dhcp-tag-rules",
            new { name = $"dup-rule-{Guid.NewGuid():N}" }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<DhcpTagRuleDetail>();
    }

    [Fact]
    public async Task DhcpEndpoints_WithoutAToken_ReturnUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/dhcp-scopes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
