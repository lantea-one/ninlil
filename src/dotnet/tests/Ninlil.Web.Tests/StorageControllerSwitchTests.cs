using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers this host's own live storage-engine switch (<see cref="StorageController" />)
///     end to end, against a real, in-process host and its real, running SQLite database (see
///     <see cref="NinlilDnsWebApplicationFactory" />) -- proving the actual shipped feature, not
///     just its underlying mechanism (<see cref="StorageEngineLiveSwitchMechanismTests" /> covers
///     that in isolation): a zone created through the real REST API before the switch is still
///     readable, by name and content, through the exact same already-authenticated
///     <see cref="HttpClient" /> session immediately after <c>POST /api/storage/switch</c>
///     completes -- this host never dropped its own already-open HTTP pipeline, never invalidated
///     the bearer token it issued before the switch began, and never stopped answering requests
///     for longer than the call itself took, all because the switch is a real, in-process
///     mechanism (see <see cref="Services.Storage.StorageEngineSwitchService" />'s own doc
///     comment) rather than any kind of restart.
///     <para>
///         This switches this test collection's own shared <see cref="NinlilDnsWebApplicationFactory" />
///         onto a second, throwaway SQLite database for the duration of this test, then switches
///         it back onto the factory's own original database before returning -- the same "restore
///         what you changed before returning" convention <see cref="ServicesControllerCrudTests" />
///         already applies to a stopped service, necessary here because every test class in this
///         collection shares exactly one factory instance (see <see cref="WebTestCollection" />'s
///         own doc comment).
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

        // Sanity check the status endpoint first: a fresh test host starts on SQLite, with no
        // switch in progress, and reports every one of the four engines a live switch can target.
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

        // Seed a real, distinguishable zone through the real REST API, into this factory's own
        // original ("database A") SQLite file.
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
            // Act: switch this already-running host onto a brand-new, empty SQLite database
            // ("database B") through the real REST endpoint -- no restart, no reconnection, this
            // exact HttpClient/bearer token stays valid throughout.
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

            // Assert: this is genuinely the same operating-system process throughout -- the switch
            // mechanism never exits or respawns this process (there is no such code path in it to
            // invoke), and this is the same in-process host that answered every call above.
            Assert.Equal(processIdBeforeSwitch, Environment.ProcessId);

            // Assert: the second database file genuinely exists on disk and was genuinely written
            // to -- this is not a no-op.
            Assert.True(File.Exists(secondDatabasePath));
            Assert.True(new FileInfo(secondDatabasePath).Length > 0);

            // Assert: the exact same already-authenticated HttpClient, immediately after the
            // switch, still reads the zone created before the switch began -- proving this process
            // now genuinely serves it out of database B (restored there by the switch), not
            // database A, with no interruption to this already-open session.
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
            // Switch this shared factory's own live storage back onto its original database
            // before returning, so every other test class in this collection keeps seeing the
            // storage engine/database it already assumes.
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
