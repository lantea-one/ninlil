using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;
using Ninlil.Web.ModelBinding;
using Ninlil.Web.Models;
using Ninlil.Web.Serialization;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers how enum query-string parameters bind, over HTTP on the shared host and
///     signed in as the bootstrap administrator: <c>presence</c> and <c>sort</c> of
///     <see cref="ClientsController" /> and <c>protocolVersion</c> of
///     <see cref="DhcpOptionCatalogController" />, and the <c>{kind}</c> route segment of
///     <see cref="DhcpServiceControlController" />. Each takes declared names only, in any case and
///     with surrounding whitespace; a digit string or an undefined name gives 400 with the valid
///     names, and an omitted optional parameter behaves as before. The tests assert on status codes
///     and the error text, not on row counts, because other tests share the data. One test drives
///     <see cref="EnumNameModelBinder" /> directly to prove it accepts the same spellings as the
///     request-body converter.
/// </summary>
[Collection(WebTestCollection.Name)]
public class QueryEnumNameTests(NinlilDnsWebApplicationFactory factory)
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

    /// <summary>
    ///     Each case is a parameter, a value that must be refused and the names the message lists.
    ///     The digit values are defined members' numbers, which MVC's own enum binder accepts.
    /// </summary>
    public static TheoryData<string, string, string> RefusedValues =>
        new()
        {
            { "/api/clients", "presence", "0" },
            { "/api/clients", "presence", "1" },
            { "/api/clients", "presence", "7" },
            { "/api/clients", "presence", "Sideways" },
            { "/api/clients", "sort", "0" },
            { "/api/clients", "sort", "2" },
            { "/api/clients", "sort", "99" },
            { "/api/clients", "sort", "Newest" },
            { "/api/dhcp-option-catalog", "protocolVersion", "4" },
            { "/api/dhcp-option-catalog", "protocolVersion", "6" },
            { "/api/dhcp-option-catalog", "protocolVersion", "5" },
            { "/api/dhcp-option-catalog", "protocolVersion", "IPv5" },
        };

    [Theory]
    [MemberData(nameof(RefusedValues))]
    public async Task Get_WithADigitStringOrAnUndefinedName_ReturnsBadRequestListingTheNames(
        string path,
        string parameter,
        string value
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string separator = path.Contains('?') ? "&" : "?";

        HttpResponseMessage response = await client.GetAsync(
            $"{path}{separator}{parameter}={Uri.EscapeDataString(value)}"
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        string[] expectedNames = parameter switch
        {
            "presence" => ["Online", "Offline"],
            "sort" => ["Created", "LastSeen", "DisplayName"],
            _ => ["IPv4", "IPv6"],
        };
        Assert.All(expectedNames, name => Assert.Contains(name, body));
    }

    [Theory]
    [InlineData("presence", "Online")]
    [InlineData("presence", "online")]
    [InlineData("presence", "OFFLINE")]
    [InlineData("presence", " Offline ")]
    [InlineData("sort", "Created")]
    [InlineData("sort", "lastseen")]
    [InlineData("sort", "DISPLAYNAME")]
    [InlineData("sort", " LastSeen ")]
    public async Task ClientList_WithAValidName_IsAcceptedInAnyCase(string parameter, string value)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/clients?{parameter}={Uri.EscapeDataString(value)}"
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("IPv4")]
    [InlineData("ipv6")]
    [InlineData("IPV4")]
    [InlineData(" IPv6 ")]
    public async Task OptionCatalog_WithAValidName_IsAcceptedInAnyCase(string value)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/dhcp-option-catalog?protocolVersion={Uri.EscapeDataString(value)}"
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/clients")]
    [InlineData("/api/clients?presence=&sort=")]
    public async Task ClientList_WithTheOptionalParametersOmittedOrBlank_ReturnsTheList(string url)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OptionCatalog_WithTheParameterBlank_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            "/api/dhcp-option-catalog?protocolVersion="
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("4")]
    [InlineData("Dhcpv7")]
    public async Task ServiceControl_WithADigitStringOrAnUndefinedKind_ReturnsBadRequestListingTheNames(
        string kind
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        foreach (string action in new[] { "start", "stop" })
        {
            HttpResponseMessage response = await client.PostAsync(
                $"/api/dhcp-services/{kind}/{action}",
                null
            );

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Dhcpv4", body);
            Assert.Contains("RouterAdvertisement", body);
            Assert.Contains("Tftp", body);
        }
    }

    // Stopping TFTP is a no-op on the shared host, where it is never started.
    [Theory]
    [InlineData("tftp")]
    [InlineData("TFTP")]
    [InlineData("Tftp")]
    public async Task ServiceControl_WithAValidKindInAnyCase_IsAccepted(string kind)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsync(
            $"/api/dhcp-services/{kind}/stop",
            null
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("?sort=LastSeen&sort=Bogus")]
    [InlineData("?sort=Bogus&sort=LastSeen")]
    [InlineData("?sort=LastSeen&sort=1")]
    public async Task ClientList_WithARepeatedParameterHoldingARefusedValue_ReturnsBadRequest(
        string query
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync($"/api/clients{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ClientList_WithARepeatedParameterOfValidNames_IsAccepted()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            "/api/clients?sort=LastSeen&sort=displayname"
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    ///     The binder and the body converter must agree on every spelling, including the
    ///     <c>EnumMember</c> wire names that differ from the member names (for example
    ///     <c>DSA-NSEC3-SHA1</c>), digit strings and unknown text.
    /// </summary>
    [Fact]
    public async Task Binder_AndBodyConverter_AcceptTheSameSpellingsForAnAliasedEnum()
    {
        JsonSerializerOptions options = new();
        options.Converters.Add(new RequestEnumNameJsonConverterFactory());

        List<string> candidates = ["0", "1", "99", "Bogus", "", " "];
        foreach (
            FieldInfo field in typeof(DnssecAlgorithmEnum).GetFields(
                BindingFlags.Public | BindingFlags.Static
            )
        )
        {
            candidates.Add(field.Name);
            candidates.Add(field.Name.ToLowerInvariant());
            string wire = field.GetCustomAttribute<EnumMemberAttribute>()?.Value;
            if (wire is not null)
            {
                candidates.Add(wire);
                candidates.Add(wire.ToLowerInvariant());
                candidates.Add($" {wire} ");
            }
        }

        EnumNameModelBinder binder = new(typeof(DnssecAlgorithmEnum));
        foreach (string candidate in candidates.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            DnssecAlgorithmEnum? fromBody = null;
            try
            {
                fromBody = JsonSerializer.Deserialize<DnssecAlgorithmEnum>(
                    JsonSerializer.Serialize(candidate),
                    options
                );
            }
            catch (JsonException) { }

            DefaultModelBindingContext context = new()
            {
                ModelName = "algorithm",
                ModelState = new ModelStateDictionary(),
                ValueProvider = new QueryStringValueProvider(
                    BindingSource.Query,
                    new Microsoft.AspNetCore.Http.QueryCollection(
                        new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
                        {
                            ["algorithm"] = candidate,
                        }
                    ),
                    System.Globalization.CultureInfo.InvariantCulture
                ),
            };
            await binder.BindModelAsync(context);

            Assert.Equal(fromBody is not null, context.Result.IsModelSet);
            if (fromBody is not null)
                Assert.Equal(fromBody, context.Result.Model);
            else
                Assert.Contains(
                    RequestEnumNameJsonConverter<DnssecAlgorithmEnum>.ValidNames,
                    context.ModelState["algorithm"].Errors.Single().ErrorMessage
                );
        }
    }
}
