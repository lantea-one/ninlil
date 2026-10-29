using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the storage-switch endpoints of <see cref="StorageController" /> over
///     HTTP on the shared in-process host, signed in as the bootstrap administrator. The switch is
///     saved for the next start and never applied here: the shared host never restarts, so no
///     data moves. Storage on that host comes from environment variables, so the origin is
///     external and the answer tells the operator which variables to set. Every test that saves a
///     switch cancels it in a <c>finally</c> block, so later tests and the next host start see no
///     pending record.
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

    private static StorageEngineSwitchApiRequest SqliteRequest(string path) =>
        new(
            StorageEngineEnum.Sqlite,
            Sqlite: new SqliteStorageConnectionFields(path),
            PostgreSql: null,
            MySql: null,
            SqlServer: null
        );

    [Fact]
    public async Task Switch_ToASecondSqliteDatabase_IsSavedForTheNextStartAndCanBeCancelled()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string secondDatabasePath = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-storage-switch-rest-{Guid.NewGuid():N}.sqlite3"
        );

        try
        {
            StorageStatus before = await (
                await client.GetAsync("/api/storage")
            ).Content.ReadFromJsonAsync<StorageStatus>();
            Assert.Equal(StorageEngineEnum.Sqlite, before.Engine);
            Assert.Null(before.PendingSwitch);
            Assert.Equal(4, before.SwitchSupportedEngines.Count);
            Assert.DoesNotContain(StorageEngineEnum.MongoDb, before.SwitchSupportedEngines);

            HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/storage/switch",
                SqliteRequest(secondDatabasePath)
            );

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            StorageSwitchScheduled scheduled =
                await response.Content.ReadFromJsonAsync<StorageSwitchScheduled>();
            Assert.Equal(StorageEngineEnum.Sqlite, scheduled.TargetEngine);
            Assert.False(scheduled.AppliesAutomatically);
            Assert.False(scheduled.TargetHasData);
            Assert.Contains(
                "NINLIL_STORAGE_ENGINE=Sqlite",
                scheduled.Note,
                StringComparison.Ordinal
            );
            Assert.Contains(
                "NINLIL_STORAGE_CONNECTION_STRING",
                scheduled.Note,
                StringComparison.Ordinal
            );

            // The pending switch is visible, without any connection string, and the running
            // process keeps its engine.
            string statusJson = await (
                await client.GetAsync("/api/storage")
            ).Content.ReadAsStringAsync();
            Assert.DoesNotContain(secondDatabasePath, statusJson, StringComparison.Ordinal);
            Assert.DoesNotContain(factory.SqlitePath, statusJson, StringComparison.Ordinal);
            StorageStatus pendingStatus = await (
                await client.GetAsync("/api/storage")
            ).Content.ReadFromJsonAsync<StorageStatus>();
            Assert.NotNull(pendingStatus.PendingSwitch);
            Assert.Equal(StorageEngineEnum.Sqlite, pendingStatus.PendingSwitch.TargetEngine);
            Assert.False(pendingStatus.PendingSwitch.AppliesAutomatically);
            Assert.False(string.IsNullOrWhiteSpace(pendingStatus.PendingSwitch.RequestedBy));
            Assert.Null(pendingStatus.LastSwitchOutcome);

            // The target file is not written: nothing is copied before the restart.
            Assert.False(File.Exists(secondDatabasePath));

            Assert.Equal(
                HttpStatusCode.NoContent,
                (await client.DeleteAsync("/api/storage/switch")).StatusCode
            );
            StorageStatus afterCancel = await (
                await client.GetAsync("/api/storage")
            ).Content.ReadFromJsonAsync<StorageStatus>();
            Assert.Null(afterCancel.PendingSwitch);
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await client.DeleteAsync("/api/storage/switch")).StatusCode
            );
        }
        finally
        {
            await client.DeleteAsync("/api/storage/switch");
            TemporaryFiles.DeleteSqliteDatabase(secondDatabasePath);
        }
    }

    [Fact]
    public async Task Switch_ToAnUnreachablePostgreSql_IsRejectedAndSavesNothing()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            // Port 9 (discard) has no PostgreSQL behind it, so the check fails fast.
            HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/storage/switch",
                new StorageEngineSwitchApiRequest(
                    StorageEngineEnum.PostgreSql,
                    Sqlite: null,
                    PostgreSql: new RelationalStorageConnectionFields(
                        "127.0.0.1",
                        9,
                        "ninlil",
                        "ninlil",
                        "a-secret-password-value",
                        "Disable"
                    ),
                    MySql: null,
                    SqlServer: null
                )
            );

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("a-secret-password-value", body, StringComparison.Ordinal);

            StorageStatus status = await (
                await client.GetAsync("/api/storage")
            ).Content.ReadFromJsonAsync<StorageStatus>();
            Assert.Null(status.PendingSwitch);
        }
        finally
        {
            await client.DeleteAsync("/api/storage/switch");
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
