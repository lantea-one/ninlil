using System.Collections;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Web.Models;
using Ninlil.Web.Serialization;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class pins the "declared names only" rule for the enum fields of typed request bodies,
///     over HTTP on the shared in-process host, signed in as the bootstrap administrator. A digit
///     string, a JSON number or an undefined value must give 400 with a message that names the
///     field and lists the valid names, and every name the admin console sends (member names, wire
///     names such as <c>RSASHA256</c>, flags lists such as <c>Sunday, Monday</c>) must still be
///     accepted. A reflection test over the API description proves that every enum type reachable
///     from a request body is read by <see cref="RequestEnumNameJsonConverter{TEnum}" />.
///     <para>
///         The host is shared with every other test class, so each test creates and removes its own
///         rows and never assumes an empty table.
///     </para>
/// </summary>
[Collection(WebTestCollection.Name)]
public class EnumNamesOnlyRequestBodyTests(NinlilDnsWebApplicationFactory factory)
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

    /// <summary>Posts raw JSON text, so a test can send a number where a name belongs.</summary>
    private static Task<HttpResponseMessage> PostRawAsync(
        HttpClient client,
        string url,
        string json
    ) => client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

    private async Task<ZoneDetail> CreateZoneAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/zones",
            new { name = $"enum-body-{Guid.NewGuid():N}.example.com", type = "Primary" }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<ZoneDetail>();
    }

    private async Task<Guid> CreateClientAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/clients",
            new
            {
                clientIdentifier = $"aa:12:{TestClientIdentifiers.Tail(4)}",
                protocolVersion = "IPv4",
            }
        );
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ClientDetail>()).Id;
    }

    private static async Task AssertBadRequestNamingAsync(
        HttpResponseMessage response,
        string field,
        string validName
    )
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains(field, body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(validName, body);
    }

    [Theory]
    [InlineData("vendorClassIdentifierMatchMode", "\"1\"")]
    [InlineData("vendorClassIdentifierMatchMode", "1")]
    [InlineData("vendorClassIdentifierMatchMode", "\"5\"")]
    [InlineData("vendorClassIdentifierMatchMode", "5")]
    [InlineData("userClassMatchMode", "\"1\"")]
    [InlineData("userClassMatchMode", "1")]
    [InlineData("userClassMatchMode", "\"5\"")]
    [InlineData("userClassMatchMode", "5")]
    public async Task CreateTagRule_WithADigitStringNumberOrUndefinedMatchMode_ReturnsBadRequest(
        string field,
        string value
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await PostRawAsync(
            client,
            "/api/dhcp-tag-rules",
            $$"""{"name":"rule-{{Guid.NewGuid():N}}","vendorClassIdentifierMatch":"x","{{field}}":{{value}}}"""
        );

        await AssertBadRequestNamingAsync(response, field, "Prefix");
    }

    [Theory]
    [InlineData("Exact", "Exact")]
    [InlineData("Prefix", "Prefix")]
    [InlineData("prefix", "Prefix")]
    public async Task CreateTagRule_WithEachMatchModeName_IsCreated(string sent, string expected)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dhcp-tag-rules",
            new
            {
                name = $"rule-{Guid.NewGuid():N}",
                vendorClassIdentifierMatch = "x",
                vendorClassIdentifierMatchMode = sent,
            }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        DhcpTagRuleDetail rule = await response.Content.ReadFromJsonAsync<DhcpTagRuleDetail>();
        try
        {
            Assert.Equal(expected, rule.VendorClassIdentifierMatchMode.ToString());
        }
        finally
        {
            await client.DeleteAsync($"/api/dhcp-tag-rules/{rule.Id}");
        }
    }

    [Theory]
    [InlineData("type", "\"0\"", "Primary")]
    [InlineData("type", "\"99\"", "Primary")]
    [InlineData("dnssecAlgorithm", "\"13\"", "ECDSAP256SHA256")]
    [InlineData("dnssecAlgorithm", "\"200\"", "ECDSAP256SHA256")]
    [InlineData("denialOfExistenceMode", "\"1\"", "NSEC3")]
    [InlineData("denialOfExistenceMode", "\"99\"", "NSEC3")]
    [InlineData("transferTransport", "\"0\"", "Plain")]
    public async Task CreateZone_WithADigitStringForAnEnumField_ReturnsBadRequest(
        string field,
        string value,
        string validName
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string type = field == "type" ? value : "\"Primary\"";
        string extra = field == "type" ? "" : $",\"{field}\":{value}";

        HttpResponseMessage response = await PostRawAsync(
            client,
            "/api/zones",
            $$"""{"name":"enum-body-{{Guid.NewGuid():N}}.example.com","type":{{type}}{{extra}}}"""
        );

        await AssertBadRequestNamingAsync(response, field, validName);
    }

    [Theory]
    [InlineData("type", "0")]
    [InlineData("type", "99")]
    [InlineData("dnssecAlgorithm", "13")]
    [InlineData("denialOfExistenceMode", "1")]
    [InlineData("transferTransport", "0")]
    public async Task CreateZone_WithAJsonNumberForAnEnumField_ReturnsBadRequest(
        string field,
        string value
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string type = field == "type" ? value : "\"Primary\"";
        string extra = field == "type" ? "" : $",\"{field}\":{value}";

        HttpResponseMessage response = await PostRawAsync(
            client,
            "/api/zones",
            $$"""{"name":"enum-body-{{Guid.NewGuid():N}}.example.com","type":{{type}}{{extra}}}"""
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateZone_WithTheConsoleWireNames_IsCreated()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/zones",
            new
            {
                name = $"enum-body-{Guid.NewGuid():N}.example.com",
                type = "Primary",
                isDnssecSigned = true,
                dnssecAlgorithm = "ECDSAP256SHA256",
                denialOfExistenceMode = "Nsec3",
                transferTransport = "Plain",
            }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        ZoneDetail zone = await response.Content.ReadFromJsonAsync<ZoneDetail>();
        Assert.Equal(
            Ninlil.Dns.Common.Enum.DnssecAlgorithmEnum.EcdsaP256Sha256,
            zone.DnssecAlgorithm
        );
        await client.DeleteAsync($"/api/zones/{zone.Id}");
    }

    [Theory]
    [InlineData("role", "\"1\"")]
    [InlineData("role", "\"9\"")]
    [InlineData("algorithm", "\"13\"")]
    [InlineData("algorithm", "\"200\"")]
    public async Task CreateDnssecKey_WithADigitStringRoleOrAlgorithm_ReturnsBadRequest(
        string field,
        string value
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        ZoneDetail zone = await CreateZoneAsync(client);

        try
        {
            string role = field == "role" ? value : "\"ZSK\"";
            string algorithm = field == "algorithm" ? value : "\"ECDSAP256SHA256\"";

            HttpResponseMessage response = await PostRawAsync(
                client,
                $"/api/zones/{zone.Id}/dnssec-keys",
                $$"""{"role":{{role}},"algorithm":{{algorithm}}}"""
            );

            await AssertBadRequestNamingAsync(
                response,
                field,
                field == "role" ? "ZSK" : "RSASHA256"
            );
        }
        finally
        {
            await client.DeleteAsync($"/api/zones/{zone.Id}");
        }
    }

    [Theory]
    [InlineData("RSASHA256")]
    [InlineData("rsasha256")]
    [InlineData("ECDSAP256SHA256")]
    public async Task CreateDnssecKey_WithWireAlgorithmName_IsCreated(string algorithm)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        ZoneDetail zone = await CreateZoneAsync(client);

        try
        {
            HttpResponseMessage response = await client.PostAsJsonAsync(
                $"/api/zones/{zone.Id}/dnssec-keys",
                new
                {
                    role = "ZSK",
                    algorithm,
                    keySizeBits = 2048,
                }
            );

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        finally
        {
            await client.DeleteAsync($"/api/zones/{zone.Id}");
        }
    }

    [Theory]
    [InlineData("type", "\"1\"")]
    [InlineData("class", "\"1\"")]
    public async Task CreateRecord_WithADigitStringTypeOrClass_ReturnsBadRequest(
        string field,
        string value
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        ZoneDetail zone = await CreateZoneAsync(client);

        try
        {
            string type = field == "type" ? value : "\"A\"";
            string recordClass = field == "class" ? value : "\"IN\"";

            HttpResponseMessage response = await PostRawAsync(
                client,
                $"/api/zones/{zone.Id}/records",
                $$$"""{"name":"www","type":{{{type}}},"class":{{{recordClass}}},"ttl":300,"data":{"address":"192.0.2.1"}}"""
            );

            await AssertBadRequestNamingAsync(response, field, field == "type" ? "A" : "IN");
        }
        finally
        {
            await client.DeleteAsync($"/api/zones/{zone.Id}");
        }
    }

    [Theory]
    [InlineData("\"3\"")]
    [InlineData("3")]
    public async Task CreateClientSchedule_WithADigitStringOrMaskDaysOfWeek_ReturnsBadRequest(
        string value
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid clientId = await CreateClientAsync(client);

        HttpResponseMessage response = await PostRawAsync(
            client,
            $"/api/clients/{clientId}/schedules",
            $$"""{"daysOfWeek":{{value}},"startTime":"21:00:00","endTime":"07:00:00","timeZoneId":"UTC"}"""
        );

        await AssertBadRequestNamingAsync(response, "daysOfWeek", "Sunday");
    }

    [Theory]
    [InlineData("Sunday, Monday")]
    [InlineData("Friday")]
    public async Task CreateClientSchedule_WithACommaListOfDayNames_StoresIt(string days)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid clientId = await CreateClientAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/clients/{clientId}/schedules",
            new
            {
                daysOfWeek = days,
                startTime = "21:00:00",
                endTime = "07:00:00",
                timeZoneId = "UTC",
            }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(body);
        Assert.Equal(days, document.RootElement.GetProperty("daysOfWeek").GetString());
    }

    [Theory]
    [InlineData("\"0\"")]
    [InlineData("\"1\"")]
    [InlineData("1")]
    public async Task AdminResetPassword_WithADigitStringMode_ReturnsBadRequest(string value)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await PostRawAsync(
            client,
            $"/api/users/{Guid.NewGuid()}/reset-password",
            $$"""{"mode":{{value}}}"""
        );

        await AssertBadRequestNamingAsync(response, "mode", "AutoGenerate");
    }

    [Fact]
    public void RequestBodies_EveryEnumPropertyType_IsReadByTheNameOnlyConverter()
    {
        IApiDescriptionGroupCollectionProvider provider =
            factory.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>();
        JsonSerializerOptions mvcOptions = factory
            .Services.GetRequiredService<IOptions<JsonOptions>>()
            .Value.JsonSerializerOptions;

        // Walk every body parameter of every action, through properties, list elements and
        // dictionary values, and collect each enum type found.
        HashSet<Type> visited = [];
        HashSet<Type> enums = [];
        foreach (
            ApiParameterDescription parameter in provider
                .ApiDescriptionGroups.Items.SelectMany(g => g.Items)
                .SelectMany(d => d.ParameterDescriptions)
                .Where(p => p.Source == BindingSource.Body)
        )
            Collect(parameter.Type, visited, enums);

        // The walk must reach known body enums, or it would pass while finding nothing.
        Assert.Contains(typeof(Ninlil.Dns.Common.Enum.ZoneTypeEnum), enums);
        Assert.Contains(typeof(Ninlil.Dns.Common.Enum.DnsScheduleDayOfWeekEnum), enums);
        Assert.Contains(typeof(Ninlil.Dns.Common.Enum.DnsResourceRecordTypeEnum), enums);
        Assert.Contains(typeof(AdminPasswordResetMode), enums);

        RequestEnumNameJsonConverterFactory factoryUnderTest = new();
        foreach (Type enumType in enums)
        {
            Assert.True(
                factoryUnderTest.CanConvert(enumType),
                $"{enumType.FullName} is read by a request body but the names-only converter does not handle it."
            );
            Assert.True(
                mvcOptions.GetConverter(enumType).GetType().IsGenericType
                    && mvcOptions.GetConverter(enumType).GetType().GetGenericTypeDefinition()
                        == typeof(RequestEnumNameJsonConverter<>),
                $"MVC reads {enumType.FullName} without the names-only converter."
            );
        }
    }

    private static void Collect(Type type, HashSet<Type> visited, HashSet<Type> enums)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (!visited.Add(type))
            return;

        if (type.IsEnum)
        {
            enums.Add(type);
            return;
        }

        if (type.IsArray)
        {
            Collect(type.GetElementType(), visited, enums);
            return;
        }

        if (type.IsGenericType)
        {
            foreach (Type argument in type.GetGenericArguments())
                Collect(argument, visited, enums);

            if (type.Namespace?.StartsWith("System") == true)
                return;
        }

        // Models from any Ninlil.* assembly are walked, because a body model can live outside
        // Ninlil.Web. Types from the BCL and other frameworks are skipped: they hold no body enums.
        if (
            type.Assembly.GetName().Name?.StartsWith("Ninlil.", StringComparison.Ordinal) != true
            || typeof(IEnumerable).IsAssignableFrom(type)
        )
            return;

        foreach (
            PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        )
            Collect(property.PropertyType, visited, enums);
    }
}
