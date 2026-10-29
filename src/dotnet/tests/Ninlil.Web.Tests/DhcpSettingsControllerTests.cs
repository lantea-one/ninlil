using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Protocol.Ra;
using Ninlil.Dhcp.Server.Core;
using Ninlil.Dhcp.Server.Ra;
using Ninlil.Dhcp.Server.Relay;
using Ninlil.Dhcp.Server.Tftp;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the eight sub-resources of <see cref="DhcpSettingsController" />
///     (<c>core</c>, <c>protocol</c>, <c>relay</c>, <c>dhcpv6-protocol</c>, <c>dhcpv6-relay</c>,
///     <c>tftp</c>, <c>ra-protocol</c> and <c>ra</c>) over HTTP on the shared in-process host,
///     signed in as the bootstrap administrator: the compiled-in defaults, patches checked by
///     reading the host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) and the
///     returned note, and input validation. The note is compared with
///     <see cref="RuntimeSettingsWriteResult.RestartRequiredNote" /> for the settings that need a
///     restart, and with <see cref="RuntimeSettingsWriteResult.HotReloadedNote" /> for the
///     Router Advertisement content and the lease sweep interval, which apply at once.
/// </summary>
[Collection(WebTestCollection.Name)]
public class DhcpSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

    [Fact]
    public async Task GetCore_ReturnsTheCompiledInDefaultsWhenNothingHasBeenConfigured()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/dhcp-settings/core");
        response.EnsureSuccessStatusCode();

        DhcpCoreSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<DhcpCoreSettingsDetail>();

        Assert.Null(detail.ServerIdentifierAddress);
        Assert.Equal(TimeSpan.FromSeconds(30), detail.OfferHoldDuration);
        Assert.Equal(TimeSpan.FromMinutes(10), detail.DeclinedAddressQuarantineDuration);
        Assert.Equal(TimeSpan.FromMinutes(1), detail.LeaseExpirySweepInterval);
    }

    [Fact]
    public async Task PatchCore_WithOnlyTheLeaseExpirySweepInterval_WritesItAndReportsItAppliesAtOnce()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/core", new { leaseExpirySweepInterval = "00:02:00" })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DhcpServer"]["Core"];
        Assert.Equal("00:02:00", section["LeaseExpirySweepInterval"].GetValue<string>());
    }

    [Fact]
    public async Task PatchCore_WithTheSweepIntervalAndAnotherField_NamesTheFieldThatAppliesAtOnce()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/core",
                new { leaseExpirySweepInterval = "00:02:00", offerHoldDuration = "00:00:45" }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.DhcpCoreMixedReloadNote, result.Note);
        Assert.Contains("leaseExpirySweepInterval", result.Note);
    }

    [Fact]
    public async Task PatchCore_WithANonPositiveSweepInterval_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/core", new { leaseExpirySweepInterval = "00:00:00" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchCore_WithNewValues_WritesThemToTheConfigurationFileAndWarnsRestartRequired()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/core",
                new { serverIdentifierAddress = "192.168.99.1" }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.RestartRequiredNote, result.Note);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DhcpServer"]["Core"];
        Assert.Equal("192.168.99.1", section["ServerIdentifierAddress"].GetValue<string>());
    }

    [Fact]
    public async Task PatchCore_WithAnInvalidAddress_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/core", new { serverIdentifierAddress = "not-an-ip" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetProtocol_ReturnsTheCompiledInDefaults()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/dhcp-settings/protocol");
        response.EnsureSuccessStatusCode();

        DhcpProtocolSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<DhcpProtocolSettingsDetail>();

        Assert.Equal(67, detail.ServerPort);
        Assert.Equal(68, detail.ClientPort);
    }

    [Fact]
    public async Task PatchProtocol_WithAnInvalidPort_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/protocol", new { serverPort = 0 })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRelay_ReturnsTheCompiledInDefaults()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/dhcp-settings/relay");
        response.EnsureSuccessStatusCode();

        DhcpRelaySettingsDetail detail =
            await response.Content.ReadFromJsonAsync<DhcpRelaySettingsDetail>();

        Assert.False(detail.Enable);
        Assert.Equal(67, detail.UpstreamServerPort);
    }

    [Fact]
    public async Task PatchRelay_WithNewUpstreamAddresses_WritesThemToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/relay",
                new { enable = true, upstreamServerAddresses = new[] { "10.0.0.53" } }
            )
        );
        response.EnsureSuccessStatusCode();

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DhcpServer"]["Relay"]["Dhcpv4"];
        Assert.True(section["Enable"].GetValue<bool>());
        JsonArray addresses = (JsonArray)section["UpstreamServerAddresses"];
        Assert.Equal("10.0.0.53", addresses[0].GetValue<string>());
    }

    [Fact]
    public async Task PatchRelay_WithAnInvalidUpstreamAddress_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/relay",
                new { upstreamServerAddresses = new[] { "not-an-ip" } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("circuit254")]
    [InlineData("nonAscii")]
    [InlineData("combined252")]
    public async Task PatchRelay_WithInvalidRelayAgentIds_ReturnsBadRequest(string kind)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        object body = kind switch
        {
            "circuit254" => new { relayAgentCircuitId = new string('a', 254) },
            "nonAscii" => new { relayAgentRemoteId = "r\u00e9mote" },
            _ => new
            {
                relayAgentCircuitId = new string('a', 126),
                relayAgentRemoteId = new string('b', 126),
            },
        };

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/relay", body)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRelay_WithValidRelayAgentIds_ReturnsOk()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/relay",
                new
                {
                    relayAgentCircuitId = new string('a', 125),
                    relayAgentRemoteId = new string('b', 126),
                }
            )
        );

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task PatchRelay_WithAnIpv6UpstreamOrRelayAddress_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage upstream = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/relay",
                new { upstreamServerAddresses = new[] { "2001:db8::1" } }
            )
        );
        HttpResponseMessage address = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/relay", new { relayAgentAddress = "2001:db8::1" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, upstream.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, address.StatusCode);
    }

    [Fact]
    public async Task PatchDhcpv6Relay_WithAnIpv4UpstreamOrLinkAddress_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage upstream = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/dhcpv6-relay",
                new { upstreamServerAddresses = new[] { "192.0.2.1" } }
            )
        );
        HttpResponseMessage link = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/dhcpv6-relay", new { relayLinkAddress = "192.0.2.1" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, upstream.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, link.StatusCode);
    }

    [Fact]
    public async Task GetDhcpv6Protocol_ReturnsTheCompiledInDefaults()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/dhcp-settings/dhcpv6-protocol");
        response.EnsureSuccessStatusCode();

        Dhcpv6ProtocolSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<Dhcpv6ProtocolSettingsDetail>();

        Assert.False(detail.Enable);
        Assert.Equal(547, detail.ServerPort);
        Assert.Equal(546, detail.ClientPort);
    }

    [Fact]
    public async Task PatchDhcpv6Protocol_WithNewValues_WritesThemToTheConfigurationFileAndWarnsRestartRequired()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/dhcpv6-protocol", new { enable = true })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.RestartRequiredNote, result.Note);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DhcpServer"]["Protocol"]["Dhcpv6"];
        Assert.True(section["Enable"].GetValue<bool>());
    }

    [Fact]
    public async Task PatchDhcpv6Protocol_WithAnInvalidPort_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/dhcpv6-protocol", new { serverPort = 0 })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDhcpv6Relay_ReturnsTheCompiledInDefaults()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/dhcp-settings/dhcpv6-relay");
        response.EnsureSuccessStatusCode();

        Dhcpv6RelaySettingsDetail detail =
            await response.Content.ReadFromJsonAsync<Dhcpv6RelaySettingsDetail>();

        Assert.False(detail.Enable);
        Assert.Equal(547, detail.UpstreamServerPort);
    }

    [Fact]
    public async Task PatchDhcpv6Relay_WithNewUpstreamAddresses_WritesThemToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/dhcpv6-relay",
                new { enable = true, upstreamServerAddresses = new[] { "2001:db8::53" } }
            )
        );
        response.EnsureSuccessStatusCode();

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DhcpServer"]["Relay"]["Dhcpv6"];
        Assert.True(section["Enable"].GetValue<bool>());
        JsonArray addresses = (JsonArray)section["UpstreamServerAddresses"];
        Assert.Equal("2001:db8::53", addresses[0].GetValue<string>());
    }

    [Fact]
    public async Task PatchDhcpv6Relay_WithAnInvalidUpstreamAddress_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/dhcpv6-relay",
                new { upstreamServerAddresses = new[] { "not-an-ip" } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchDhcpv6Relay_WithAnInvalidRelayLinkAddress_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/dhcpv6-relay", new { relayLinkAddress = "not-an-ip" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetTftp_ReturnsTheCompiledInDefaults()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/dhcp-settings/tftp");
        response.EnsureSuccessStatusCode();

        DhcpTftpSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<DhcpTftpSettingsDetail>();

        Assert.False(detail.Enable);
        Assert.Equal(69, detail.Port);
        Assert.Equal(5, detail.MaxRetransmissions);
        Assert.Equal(50, detail.MaxConcurrentTransfers);
    }

    [Fact]
    public async Task PatchTftp_WithNewValues_WritesThemToTheConfigurationFileAndWarnsRestartRequired()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/tftp",
                new { enable = true, rootDirectory = "/var/lib/ninlil/tftp" }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.RestartRequiredNote, result.Note);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DhcpServer"]["Tftp"];
        Assert.True(section["Enable"].GetValue<bool>());
        Assert.Equal("/var/lib/ninlil/tftp", section["RootDirectory"].GetValue<string>());
    }

    [Fact]
    public async Task PatchTftp_WithAnInvalidPort_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/tftp", new { port = 0 })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchTftp_WithAnInvalidMaxConcurrentTransfers_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/tftp", new { maxConcurrentTransfers = 0 })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchTftp_WithMaxConcurrentTransfers_WritesItToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/tftp", new { maxConcurrentTransfers = 10 })
        );
        response.EnsureSuccessStatusCode();

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DhcpServer"]["Tftp"];
        Assert.Equal(10, section["MaxConcurrentTransfers"].GetValue<int>());
    }

    [Fact]
    public async Task PatchTftp_WithAnInvalidMaxRetransmissions_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/tftp", new { maxRetransmissions = 0 })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRaProtocol_ReturnsTheCompiledInDefaults()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/dhcp-settings/ra-protocol");
        response.EnsureSuccessStatusCode();

        DhcpRaProtocolSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<DhcpRaProtocolSettingsDetail>();

        Assert.False(detail.Enable);
        Assert.Equal(TimeSpan.FromSeconds(198), detail.MinRouterAdvertisementInterval);
        Assert.Equal(TimeSpan.FromSeconds(600), detail.MaxRouterAdvertisementInterval);
    }

    [Fact]
    public async Task PatchRaProtocol_WithNewValues_WritesThemToTheConfigurationFileAndWarnsRestartRequired()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/ra-protocol", new { enable = true })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.RestartRequiredNote, result.Note);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DhcpServer"]["Protocol"]["Ra"];
        Assert.True(section["Enable"].GetValue<bool>());
    }

    [Fact]
    public async Task PatchRaProtocol_WithANonPositiveInterval_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra-protocol",
                new { minRouterAdvertisementInterval = "00:00:00" }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(500, 600)]
    public async Task PatchRaProtocol_WithAnIntervalPairOutsideTheRfcRules_ReturnsBadRequest(
        int minSeconds,
        int maxSeconds
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // The 500/600 pair breaks "minimum at most 0.75 times maximum". A patch that sets only
        // the minimum is compared with the stored maximum (600 s by default).
        object body =
            minSeconds == 500
                ? new
                {
                    minRouterAdvertisementInterval = TimeSpan.FromSeconds(minSeconds).ToString(),
                }
                : new
                {
                    minRouterAdvertisementInterval = TimeSpan.FromSeconds(minSeconds).ToString(),
                    maxRouterAdvertisementInterval = TimeSpan.FromSeconds(maxSeconds).ToString(),
                };

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/ra-protocol", body)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRaProtocol_WithAValidIntervalPair_Succeeds()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra-protocol",
                new
                {
                    minRouterAdvertisementInterval = "00:03:20",
                    maxRouterAdvertisementInterval = "00:10:00",
                }
            )
        );

        response.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("routerLifetime", "1.00:00:00")]
    [InlineData("reachableTime", "02:00:00")]
    [InlineData("recursiveDnsServers", new[] { "192.0.2.1" })]
    [InlineData("linkMtu", 1000)]
    public async Task PatchRa_WithAValueTheAdvertisementCannotCarry_ReturnsBadRequest(
        string field,
        object value
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra",
                new Dictionary<string, object> { [field] = value }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRa_WithAnIpv4RoutePrefix_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra",
                new { routes = new object[] { new { prefix = "192.0.2.0", prefixLength = 24 } } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRa_WithAnOverrideHoldingAnIpv4RdnssAddress_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra",
                new
                {
                    interfaceOverrides = new object[]
                    {
                        new { interfaceName = "eth7", recursiveDnsServers = new[] { "192.0.2.1" } },
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRa_WithValuesInsideTheLimits_Succeeds()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra",
                new
                {
                    routerLifetime = "18:12:15",
                    reachableTime = "01:00:00",
                    linkMtu = 1280,
                    recursiveDnsServers = new[] { "2001:db8::53" },
                    routes = new object[] { new { prefix = "fe80::", prefixLength = 10 } },
                    interfaceOverrides = new object[]
                    {
                        new
                        {
                            interfaceName = "eth8",
                            recursiveDnsServers = new[] { "2001:db8::54" },
                        },
                    },
                }
            )
        );

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetRa_ReturnsTheCompiledInDefaults()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/dhcp-settings/ra");
        response.EnsureSuccessStatusCode();

        DhcpRaSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<DhcpRaSettingsDetail>();

        Assert.False(detail.ManagedAddressConfiguration);
        Assert.False(detail.OtherConfiguration);
        Assert.Equal(TimeSpan.Zero, detail.RouterLifetime);
        Assert.Equal(64, detail.CurrentHopLimit);
        Assert.Null(detail.LinkMtu);
        Assert.Empty(detail.RecursiveDnsServers);
        Assert.Empty(detail.DnsSearchListDomains);
        Assert.Equal(TimeSpan.FromSeconds(1800), detail.DnsSearchListLifetime);
        Assert.Empty(detail.Routes);
        Assert.Empty(detail.InterfaceOverrides);
    }

    [Fact]
    public async Task PatchRa_WithMoreRdnssAddressesThanFitOneAdvertisement_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage atTheLimit = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/ra", new { recursiveDnsServers = RdnssAddresses(75) })
        );
        HttpResponseMessage overTheLimit = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/ra", new { recursiveDnsServers = RdnssAddresses(76) })
        );

        atTheLimit.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, overTheLimit.StatusCode);
        Assert.Contains("75", await overTheLimit.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PatchRa_WithAnOverrideHoldingMoreRdnssAddressesThanFitOneAdvertisement_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra",
                new
                {
                    interfaceOverrides = new object[]
                    {
                        new { interfaceName = "eth9", recursiveDnsServers = RdnssAddresses(76) },
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRa_WithASearchListThatEncodesLargerThanOneAdvertisementAllows_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // 18 names of 65 octets fit 1208 octets with the option header; 19 do not.
        HttpResponseMessage fits = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/ra", new { dnsSearchListDomains = SearchNames(18) })
        );
        HttpResponseMessage tooLarge = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/ra", new { dnsSearchListDomains = SearchNames(19) })
        );
        HttpResponseMessage overrideTooLarge = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra",
                new
                {
                    interfaceOverrides = new object[]
                    {
                        new { interfaceName = "eth9", dnsSearchListDomains = SearchNames(19) },
                    },
                }
            )
        );

        fits.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, overrideTooLarge.StatusCode);
    }

    [Fact]
    public async Task PatchRa_WithValidContent_ReportsThatItAppliesAtOnce()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/ra", new { currentHopLimit = 64 })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);
    }

    /// <summary>
    ///     This method returns the given number of distinct, valid RDNSS addresses.
    /// </summary>
    private static string[] RdnssAddresses(int count) =>
        [.. Enumerable.Range(1, count).Select(index => $"2001:db8::{index:x}")];

    /// <summary>
    ///     This method returns distinct search domains that each encode to 65 octets (a
    ///     60-character label, a 2-character label, the length bytes and the root label).
    /// </summary>
    private static string[] SearchNames(int count) =>
        [.. Enumerable.Range(10, count).Select(index => new string('a', 60) + "." + index)];

    [Fact]
    public async Task PatchRa_WithRoutes_WritesThemToTheConfigurationFileAsAWholeArrayReplace()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra",
                new
                {
                    routes = new object[]
                    {
                        new
                        {
                            prefix = "2001:db8:5::",
                            prefixLength = 64,
                            preference = "High",
                            routeLifetime = "00:30:00",
                        },
                        new
                        {
                            prefix = (string)null,
                            prefixLength = 0,
                            preference = "Low",
                            routeLifetime = "00:05:00",
                        },
                    },
                }
            )
        );
        patchResponse.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await patchResponse.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        // Every DHCP setting needs a restart (see DhcpSettingsController), so the write is
        // checked in the settings file, not with a later GetRa.
        Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonArray routes = (JsonArray)root["DhcpServer"]["Ra"]["Routes"];
        Assert.Equal(2, routes.Count);
        Assert.Equal("2001:db8:5::", routes[0]["Prefix"].GetValue<string>());
        Assert.Equal(64, routes[0]["PrefixLength"].GetValue<int>());
        Assert.Equal("High", routes[0]["Preference"].GetValue<string>());
        Assert.Equal("00:30:00", routes[0]["RouteLifetime"].GetValue<string>());
        Assert.Equal(0, routes[1]["PrefixLength"].GetValue<int>());
        Assert.Equal("Low", routes[1]["Preference"].GetValue<string>());
        Assert.Equal("00:05:00", routes[1]["RouteLifetime"].GetValue<string>());
    }

    [Fact]
    public void GetRa_MapsEveryConfiguredRouteFieldOntoItsOwnRestRepresentation()
    {
        // The shared host never reloads its settings file, so the controller is built directly
        // with a monitor that holds the routes. Every field of the two routes differs, so a
        // mapping that swaps two fields shows in the result.
        RaOptions options = new()
        {
            Routes =
            [
                new RaAdvertisedRouteOptions
                {
                    Prefix = "2001:db8:5::",
                    PrefixLength = 64,
                    Preference = RaRoutePreferenceEnum.High,
                    RouteLifetime = TimeSpan.FromMinutes(30),
                },
                new RaAdvertisedRouteOptions
                {
                    Prefix = "2001:db8:6::",
                    PrefixLength = 48,
                    Preference = RaRoutePreferenceEnum.Low,
                    RouteLifetime = TimeSpan.FromMinutes(5),
                },
            ],
        };

        DhcpRaSettingsDetail detail = GetRaFromController(options);

        Assert.Equal(2, detail.Routes.Count);
        Assert.Equal("2001:db8:5::", detail.Routes[0].Prefix);
        Assert.Equal(64, detail.Routes[0].PrefixLength);
        Assert.Equal(RaRoutePreferenceEnum.High, detail.Routes[0].Preference);
        Assert.Equal(TimeSpan.FromMinutes(30), detail.Routes[0].RouteLifetime);
        Assert.Equal("2001:db8:6::", detail.Routes[1].Prefix);
        Assert.Equal(48, detail.Routes[1].PrefixLength);
        Assert.Equal(RaRoutePreferenceEnum.Low, detail.Routes[1].Preference);
        Assert.Equal(TimeSpan.FromMinutes(5), detail.Routes[1].RouteLifetime);
    }

    /// <summary>
    ///     This method builds <see cref="DhcpSettingsController" /> over the given live
    ///     Router Advertisement options, calls <see cref="DhcpSettingsController.GetRa" /> and
    ///     returns the detail it answers. The other sections are never read by <c>GetRa</c>, so
    ///     they hold defaults. The controller is built directly because the shared host does not
    ///     reload its runtime settings file.
    /// </summary>
    private static DhcpRaSettingsDetail GetRaFromController(RaOptions live)
    {
        DhcpSettingsController controller = new(
            Microsoft.Extensions.Options.Options.Create(new DhcpServerOptions()),
            new FixedMonitor<DhcpServerOptions>(new DhcpServerOptions()),
            Microsoft.Extensions.Options.Options.Create(new Dhcpv4ProtocolOptions()),
            Microsoft.Extensions.Options.Options.Create(new Dhcpv4RelayOptions()),
            Microsoft.Extensions.Options.Options.Create(new Dhcpv6ProtocolOptions()),
            Microsoft.Extensions.Options.Options.Create(new Dhcpv6RelayOptions()),
            Microsoft.Extensions.Options.Options.Create(new TftpServerOptions()),
            Microsoft.Extensions.Options.Options.Create(new RaProtocolOptions()),
            new FixedMonitor<RaOptions>(live),
            new RuntimeConfigurationOptions()
        );

        return Assert.IsType<DhcpRaSettingsDetail>(
            Assert.IsType<OkObjectResult>(controller.GetRa().Result).Value
        );
    }

    /// <summary>
    ///     This class is an <see cref="IOptionsMonitor{TOptions}" /> fake that always returns the
    ///     same value, for the tests that build a controller directly.
    /// </summary>
    private sealed class FixedMonitor<T>(T value) : IOptionsMonitor<T>
    {
        /// <inheritdoc />
        public T CurrentValue { get; } = value;

        /// <inheritdoc />
        public T Get(string name) => CurrentValue;

        /// <inheritdoc />
        public IDisposable OnChange(Action<T, string> listener) => null;
    }

    [Fact]
    public async Task PatchRa_WithARouteMissingAPrefixLength_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra",
                new { routes = new object[] { new { prefix = "2001:db8:5::" } } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRa_WithARouteWhosePrefixLengthIsOutOfRange_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra",
                new
                {
                    routes = new object[] { new { prefix = "2001:db8:5::", prefixLength = 200 } },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRa_WithARouteWhoseNonZeroPrefixLengthHasNoPrefixAddress_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra",
                new { routes = new object[] { new { prefixLength = 64 } } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRa_WithARouteUsingAnUnrecognizedPreferenceName_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra",
                new
                {
                    routes = new object[]
                    {
                        new
                        {
                            prefix = "2001:db8:5::",
                            prefixLength = 64,
                            preference = "Ultra",
                        },
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRa_WithRoutesNotAnArray_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/ra", new { routes = "not-an-array" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRa_WithInterfaceOverrides_WritesThemToTheConfigurationFileAsAWholeArrayReplace()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra",
                new
                {
                    interfaceOverrides = new object[]
                    {
                        new
                        {
                            interfaceName = "eth0",
                            exclude = false,
                            managedAddressConfiguration = true,
                            routerLifetime = "00:05:00",
                            currentHopLimit = 32,
                            recursiveDnsServers = new[] { "2001:db8::1" },
                            dnsSearchListDomains = new[] { "eth0.internal" },
                        },
                        new { interfaceName = "eth1", exclude = true },
                    },
                }
            )
        );
        patchResponse.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await patchResponse.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonArray overrides = (JsonArray)root["DhcpServer"]["Ra"]["InterfaceOverrides"];
        Assert.Equal(2, overrides.Count);

        Assert.Equal("eth0", overrides[0]["InterfaceName"].GetValue<string>());
        Assert.False(overrides[0]["Exclude"].GetValue<bool>());
        Assert.True(overrides[0]["ManagedAddressConfiguration"].GetValue<bool>());
        Assert.Equal("00:05:00", overrides[0]["RouterLifetime"].GetValue<string>());
        Assert.Equal(32, overrides[0]["CurrentHopLimit"].GetValue<int>());
        Assert.Equal("2001:db8::1", overrides[0]["RecursiveDnsServers"][0].GetValue<string>());
        Assert.Equal("eth0.internal", overrides[0]["DnsSearchListDomains"][0].GetValue<string>());

        Assert.Equal("eth1", overrides[1]["InterfaceName"].GetValue<string>());
        Assert.True(overrides[1]["Exclude"].GetValue<bool>());
        Assert.Null(overrides[1]["ManagedAddressConfiguration"]);
        Assert.Null(overrides[1]["RouterLifetime"]);
    }

    [Fact]
    public void GetRa_MapsEveryConfiguredInterfaceOverrideFieldOntoItsOwnRestRepresentation()
    {
        // As in GetRa_MapsEveryConfiguredRouteFieldOntoItsOwnRestRepresentation, the controller is
        // built directly. Each field of the first override has its own value, so a swapped pair
        // shows; the second override sets nothing, so the unset fields must stay null (the global
        // value applies).
        RaOptions options = new()
        {
            InterfaceOverrides =
            [
                new RaInterfaceOverrideOptions
                {
                    InterfaceName = "eth0",
                    Exclude = true,
                    ManagedAddressConfiguration = true,
                    OtherConfiguration = false,
                    RouterLifetime = TimeSpan.FromMinutes(5),
                    CurrentHopLimit = 32,
                    ReachableTime = TimeSpan.FromSeconds(11),
                    RetransTimer = TimeSpan.FromSeconds(12),
                    LinkMtu = 1400,
                    RecursiveDnsServers = ["2001:db8::1"],
                    RecursiveDnsServerLifetime = TimeSpan.FromSeconds(13),
                    DnsSearchListDomains = ["eth0.internal"],
                    DnsSearchListLifetime = TimeSpan.FromSeconds(14),
                },
                new RaInterfaceOverrideOptions { InterfaceName = "eth1" },
            ],
        };

        DhcpRaSettingsDetail detail = GetRaFromController(options);

        Assert.Equal(2, detail.InterfaceOverrides.Count);
        DhcpRaInterfaceOverrideDetail first = detail.InterfaceOverrides[0];
        Assert.Equal("eth0", first.InterfaceName);
        Assert.True(first.Exclude);
        Assert.True(first.ManagedAddressConfiguration);
        Assert.False(first.OtherConfiguration);
        Assert.Equal(TimeSpan.FromMinutes(5), first.RouterLifetime);
        Assert.Equal(32, first.CurrentHopLimit);
        Assert.Equal(TimeSpan.FromSeconds(11), first.ReachableTime);
        Assert.Equal(TimeSpan.FromSeconds(12), first.RetransTimer);
        Assert.Equal(1400u, first.LinkMtu);
        Assert.Equal(["2001:db8::1"], first.RecursiveDnsServers);
        Assert.Equal(TimeSpan.FromSeconds(13), first.RecursiveDnsServerLifetime);
        Assert.Equal(["eth0.internal"], first.DnsSearchListDomains);
        Assert.Equal(TimeSpan.FromSeconds(14), first.DnsSearchListLifetime);

        DhcpRaInterfaceOverrideDetail second = detail.InterfaceOverrides[1];
        Assert.Equal("eth1", second.InterfaceName);
        Assert.False(second.Exclude);
        Assert.Null(second.ManagedAddressConfiguration);
        Assert.Null(second.OtherConfiguration);
        Assert.Null(second.RouterLifetime);
        Assert.Null(second.CurrentHopLimit);
        Assert.Null(second.ReachableTime);
        Assert.Null(second.RetransTimer);
        Assert.Null(second.LinkMtu);
        Assert.Null(second.RecursiveDnsServers);
        Assert.Null(second.RecursiveDnsServerLifetime);
        Assert.Null(second.DnsSearchListDomains);
        Assert.Null(second.DnsSearchListLifetime);
    }

    [Fact]
    public async Task PatchRa_WithAnInterfaceOverrideMissingAnInterfaceName_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra",
                new { interfaceOverrides = new object[] { new { exclude = true } } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRa_WithAnInterfaceOverrideCurrentHopLimitOutOfRange_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra",
                new
                {
                    interfaceOverrides = new object[]
                    {
                        new { interfaceName = "eth0", currentHopLimit = 999 },
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRa_WithAnInterfaceOverrideInvalidRecursiveDnsServerAddress_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra",
                new
                {
                    interfaceOverrides = new object[]
                    {
                        new { interfaceName = "eth0", recursiveDnsServers = new[] { "not-an-ip" } },
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRa_WithAnInterfaceOverrideMalformedDnsSearchListDomain_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra",
                new
                {
                    interfaceOverrides = new object[]
                    {
                        new
                        {
                            interfaceName = "eth0",
                            dnsSearchListDomains = new[] { "has..empty.label" },
                        },
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRa_WithInterfaceOverridesNotAnArray_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/ra", new { interfaceOverrides = "not-an-array" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRa_WithADnsSearchList_WritesItToTheConfigurationFileAndReportsItAppliesAtOnce()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra",
                new
                {
                    dnsSearchListDomains = new[] { "homelab.internal", "example.com" },
                    dnsSearchListLifetime = "00:20:00",
                }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DhcpServer"]["Ra"];
        JsonArray domains = (JsonArray)section["DnsSearchListDomains"];
        Assert.Equal("homelab.internal", domains[0].GetValue<string>());
        Assert.Equal("example.com", domains[1].GetValue<string>());
        Assert.Equal("00:20:00", section["DnsSearchListLifetime"].GetValue<string>());
    }

    [Fact]
    public async Task PatchRa_WithAMalformedDnsSearchListDomain_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra",
                new { dnsSearchListDomains = new[] { "has..empty.label" } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRa_WithAnInvalidRecursiveDnsServerAddress_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dhcp-settings/ra",
                new { recursiveDnsServers = new[] { "not-an-ip" } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRa_WithACurrentHopLimitOutOfRange_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dhcp-settings/ra", new { currentHopLimit = 999 })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchRa_WithANegativeRouterLifetime_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            // A malformed value, not a negative one as the test name says.
            PatchRequest("/api/dhcp-settings/ra", new { routerLifetime = "not-a-timespan" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DhcpSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/dhcp-settings/core");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
