using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Server.Ra;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="DhcpSettingsController" /> end to end, against a real,
///     in-process host and an isolated, throwaway JSON configuration file (see
///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />), for all eight
///     of its own sub-resources (<c>core</c>/<c>protocol</c>/<c>relay</c>/<c>dhcpv6-protocol</c>/
///     <c>dhcpv6-relay</c>/<c>tftp</c>/<c>ra-protocol</c>/<c>ra</c>).
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
        Assert.Contains("restart", result.Note, StringComparison.OrdinalIgnoreCase);

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
        Assert.Contains("restart", result.Note, StringComparison.OrdinalIgnoreCase);

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
        Assert.Contains("restart", result.Note, StringComparison.OrdinalIgnoreCase);

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
        Assert.Contains("restart", result.Note, StringComparison.OrdinalIgnoreCase);

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
        // Every DHCP settings section here is restart-required, with no live hot-reload at all --
        // see this controller's own doc comment -- so the write is verified directly against the
        // configuration file it was merged into, exactly like every other `PatchRa_With...` test
        // in this class, rather than by reading it back through a subsequent `GetRa` call.
        Assert.Contains("restart", result.Note, StringComparison.OrdinalIgnoreCase);

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
    public async Task GetRa_MapsEveryConfiguredRouteFieldOntoItsOwnRestRepresentation()
    {
        // Unlike the write-side test above (which is restart-required and therefore verified
        // against the configuration file directly), this proves `GetRa`'s own read-side mapping
        // from `RaOptions.Routes` onto `DhcpRaRouteDetail` -- exercised through the real, already
        // config-file-bound `RaOptions` this factory's own host starts with, by writing the config
        // file `PresetConfigurationFileApplier` itself would write and confirming a fresh read of
        // `IOptions<RaOptions>` (via a brand-new host, since this controller's own settings are
        // restart-required) reports it back correctly.
        string configPath = Path.Combine(
            Directory.CreateTempSubdirectory("ninlil-ra-route-get-test-").FullName,
            "appsettings.json"
        );
        await PresetConfigurationFileApplier.ApplyAsync(
            [
                new PresetConfigurationSetting(
                    "DhcpServer:Ra",
                    "Routes",
                    new[]
                    {
                        new
                        {
                            Prefix = "2001:db8:5::",
                            PrefixLength = 64,
                            Preference = "High",
                            RouteLifetime = "00:30:00",
                        },
                    }
                ),
            ],
            configPath
        );

        RaOptions options = new();
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile(configPath)
            .Build();
        configuration.GetSection(RaOptions.SectionName).Bind(options);

        Assert.Single(options.Routes);
        Assert.Equal("2001:db8:5::", options.Routes[0].Prefix);
        Assert.Equal(64, options.Routes[0].PrefixLength);
        Assert.Equal(RaRoutePreferenceEnum.High, options.Routes[0].Preference);
        Assert.Equal(TimeSpan.FromMinutes(30), options.Routes[0].RouteLifetime);

        Directory.Delete(Path.GetDirectoryName(configPath)!, recursive: true);
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
        Assert.Contains("restart", result.Note, StringComparison.OrdinalIgnoreCase);

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
    public async Task GetRa_MapsEveryConfiguredInterfaceOverrideFieldOntoItsOwnRestRepresentation()
    {
        // Mirrors GetRa_MapsEveryConfiguredRouteFieldOntoItsOwnRestRepresentation's own approach:
        // this is restart-required, so it is verified by writing the same configuration file
        // PresetConfigurationFileApplier itself would write and confirming a fresh read of
        // IOptions<RaOptions> reports it back correctly, rather than through this controller.
        string configPath = Path.Combine(
            Directory.CreateTempSubdirectory("ninlil-ra-interface-override-get-test-").FullName,
            "appsettings.json"
        );
        await PresetConfigurationFileApplier.ApplyAsync(
            [
                new PresetConfigurationSetting(
                    "DhcpServer:Ra",
                    "InterfaceOverrides",
                    new[]
                    {
                        new
                        {
                            InterfaceName = "eth0",
                            Exclude = false,
                            RouterLifetime = "00:05:00",
                            CurrentHopLimit = (byte)32,
                        },
                    }
                ),
            ],
            configPath
        );

        RaOptions options = new();
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile(configPath)
            .Build();
        configuration.GetSection(RaOptions.SectionName).Bind(options);

        Assert.Single(options.InterfaceOverrides);
        Assert.Equal("eth0", options.InterfaceOverrides[0].InterfaceName);
        Assert.False(options.InterfaceOverrides[0].Exclude);
        Assert.Equal(TimeSpan.FromMinutes(5), options.InterfaceOverrides[0].RouterLifetime);
        Assert.Equal((byte)32, options.InterfaceOverrides[0].CurrentHopLimit);
        // Every field this override entry never set still reports null -- inheriting the global
        // default rather than silently defaulting to some other value.
        Assert.Null(options.InterfaceOverrides[0].ManagedAddressConfiguration);
        Assert.Null(options.InterfaceOverrides[0].RecursiveDnsServers);

        Directory.Delete(Path.GetDirectoryName(configPath)!, recursive: true);
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
    public async Task PatchRa_WithADnsSearchList_WritesItToTheConfigurationFileAndWarnsRestartRequired()
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
        Assert.Contains("restart", result.Note, StringComparison.OrdinalIgnoreCase);

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
