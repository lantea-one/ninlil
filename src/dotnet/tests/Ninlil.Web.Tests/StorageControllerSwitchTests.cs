using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the live storage-engine switch of <see cref="StorageController" /> over
///     HTTP on the shared in-process host, signed in as the bootstrap administrator.
///     <see cref="StorageEngineLiveSwitchMechanismTests" /> covers the mechanism on its own. A zone
///     is created, the host is switched to a second SQLite file with <c>POST /api/storage/switch</c>,
///     and the same client, with the same bearer token, can still read the zone afterwards; the
///     switch happens in process (see <see cref="Services.Storage.StorageEngineSwitchService" />),
///     not by restarting. The test cannot tell whether the zone is read from the new file or the
///     old one; it only checks that the new file was written and the data is still readable.
///     <para>
///         The switch changes the storage of the host every test class shares, so the test
///         switches back to <see cref="NinlilDnsWebApplicationFactory.SqlitePath" /> before
///         returning, as <see cref="ServicesControllerCrudTests" /> restarts a stopped service.
///         A switch to MongoDB must be rejected.
///     </para>
/// </summary>
[Collection(WebTestCollection.Name)]
public class StorageControllerSwitchTests(NinlilDnsWebApplicationFactory factory)
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

    [Fact]
    public async Task Switch_ToASecondSqliteDatabase_CarriesDataAcrossWithNoRestartAndNoInterruption()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // The host starts on SQLite with no switch in progress, and reports the four engines a
        // live switch can target (not MongoDB).
        StorageStatus statusBeforeSwitch = await (
            await client.GetAsync("/api/storage")
        ).Content.ReadFromJsonAsync<StorageStatus>();
        Assert.Equal(StorageEngineEnum.Sqlite, statusBeforeSwitch.Engine);
        Assert.False(statusBeforeSwitch.SwitchInProgress);
        Assert.Equal(4, statusBeforeSwitch.LiveSwitchSupportedEngines.Count);
        Assert.DoesNotContain(
            StorageEngineEnum.MongoDb,
            statusBeforeSwitch.LiveSwitchSupportedEngines
        );

        // Create a distinctive zone through the API in the original SQLite file ("database A").
        string zoneName = $"storage-switch-{Guid.NewGuid():N}.example.com";
        HttpResponseMessage createZoneResponse = await client.PostAsJsonAsync(
            "/api/zones",
            new { name = zoneName, type = "Primary" }
        );
        Assert.Equal(HttpStatusCode.Created, createZoneResponse.StatusCode);
        ZoneDetail createdZone = await createZoneResponse.Content.ReadFromJsonAsync<ZoneDetail>();

        int processIdBeforeSwitch = Environment.ProcessId;

        string secondDatabasePath = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-storage-switch-rest-{Guid.NewGuid():N}.sqlite3"
        );

        try
        {
            // Switch the running host to a new, empty SQLite file ("database B") through the API;
            // the same HttpClient and bearer token are used throughout.
            HttpResponseMessage switchResponse = await client.PostAsJsonAsync(
                "/api/storage/switch",
                new StorageEngineSwitchApiRequest(
                    StorageEngineEnum.Sqlite,
                    Sqlite: new SqliteStorageConnectionFields(secondDatabasePath),
                    PostgreSql: null,
                    MySql: null,
                    SqlServer: null
                )
            );
            Assert.Equal(HttpStatusCode.OK, switchResponse.StatusCode);

            StorageEngineSwitchResult switchResult =
                await switchResponse.Content.ReadFromJsonAsync<StorageEngineSwitchResult>();
            Assert.Equal(StorageEngineEnum.Sqlite, switchResult.PreviousEngine);
            Assert.Equal(StorageEngineEnum.Sqlite, switchResult.NewEngine);
            Assert.True(switchResult.TotalEntitiesRestored > 0);
            Assert.True(switchResult.EntityCounts.TryGetValue("Zones", out int restoredZoneCount));
            Assert.True(restoredZoneCount >= 1);

            // The process is the same. In an in-process host this always holds, so it only documents
            // that the switch does not restart anything.
            Assert.Equal(processIdBeforeSwitch, Environment.ProcessId);

            // The second database file exists and was written to.
            Assert.True(File.Exists(secondDatabasePath));
            Assert.True(new FileInfo(secondDatabasePath).Length > 0);

            // The same client still reads the zone created before the switch. Database A still
            // holds the zone too, so this does not show which file served it.
            HttpResponseMessage getZoneResponse = await client.GetAsync(
                $"/api/zones/{createdZone.Id}"
            );
            Assert.Equal(HttpStatusCode.OK, getZoneResponse.StatusCode);
            ZoneDetail zoneAfterSwitch =
                await getZoneResponse.Content.ReadFromJsonAsync<ZoneDetail>();
            Assert.Equal(zoneName, zoneAfterSwitch.Name);

            StorageStatus statusAfterSwitch = await (
                await client.GetAsync("/api/storage")
            ).Content.ReadFromJsonAsync<StorageStatus>();
            Assert.Equal(StorageEngineEnum.Sqlite, statusAfterSwitch.Engine);
            Assert.False(statusAfterSwitch.SwitchInProgress);
        }
        finally
        {
            // Switch back to the original database, which the other test classes expect.
            HttpResponseMessage switchBackResponse = await client.PostAsJsonAsync(
                "/api/storage/switch",
                new StorageEngineSwitchApiRequest(
                    StorageEngineEnum.Sqlite,
                    Sqlite: new SqliteStorageConnectionFields(factory.SqlitePath),
                    PostgreSql: null,
                    MySql: null,
                    SqlServer: null
                )
            );
            switchBackResponse.EnsureSuccessStatusCode();

            File.Delete(secondDatabasePath);
        }
    }

    [Fact]
    public async Task Switch_ToMongoDb_IsRejectedWithAClearMessageRatherThanAttemptingSomethingUnproven()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/storage/switch",
            new StorageEngineSwitchApiRequest(
                StorageEngineEnum.MongoDb,
                Sqlite: null,
                PostgreSql: null,
                MySql: null,
                SqlServer: null
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("MongoDB", body, StringComparison.Ordinal);
    }
}
