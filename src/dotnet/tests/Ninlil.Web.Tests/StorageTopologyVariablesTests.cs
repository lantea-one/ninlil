using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ninlil.Common.Configuration;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Web.EnvironmentVariables;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <c>StorageStatus.TopologyVariables</c> of <c>GET /api/storage</c> on the
///     shared in-process host: the list must hold exactly the environment variables the
///     options-to-environment mapping holds for the five storage topology sections, computed here
///     from the mapping instead of typed out, so the page that prints the list cannot drift from
///     what the server reads.
/// </summary>
[Collection(WebTestCollection.Name)]
public class StorageTopologyVariablesTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This method reads the topology variables from <c>GET /api/storage</c> as the
    ///     bootstrap administrator.
    /// </summary>
    private async Task<List<string>> ReadTopologyVariablesAsync()
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

        JsonElement root = await client.GetFromJsonAsync<JsonElement>("/api/storage");

        return
        [
            .. root.GetProperty("topologyVariables").EnumerateArray().Select(e => e.GetString()),
        ];
    }

    [Fact]
    public async Task Status_ListsExactlyTheVariablesTheMappingHoldsForTheTopologySections()
    {
        string[] sections = ["ReadReplica", "PostgreSql", "MySql", "SqlServer", "MongoDb"];
        IReadOnlyDictionary<string, string> mappings = OptionsEnvironmentVariableMap.BuildMappings(
            OptionsEnvironmentVariableRegistry.Roots,
            OptionsEnvironmentVariableRegistry.StandaloneConfigurationPaths
        );
        List<string> expected =
        [
            .. mappings
                .Where(mapping =>
                    sections.Any(section =>
                        mapping.Value.StartsWith(
                            $"{StorageOptions.SectionName}:{section}:",
                            StringComparison.Ordinal
                        )
                    )
                )
                .Select(mapping => mapping.Key),
        ];

        List<string> actual = await ReadTopologyVariablesAsync();

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
        Assert.Equal(actual.Count, actual.Distinct().Count());
    }

    [Fact]
    public async Task Status_ListsSixteenVariablesIncludingTheReplicaConnectionString()
    {
        List<string> actual = await ReadTopologyVariablesAsync();

        // ENVIRONMENT.md lists 16 topology variables: 4 replica, 4 PostgreSQL, 2 MySQL, 1 SQL
        // Server and 5 MongoDB.
        Assert.Equal(16, actual.Count);
        Assert.Contains("NINLIL_STORAGE_READ_REPLICA_CONNECTION_STRING", actual);
        Assert.StartsWith("NINLIL_STORAGE_READ_REPLICA_", actual[0], StringComparison.Ordinal);
    }
}
