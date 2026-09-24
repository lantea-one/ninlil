using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the full-system backup/restore REST endpoints
///     (<see cref="BackupController" />) end to end, against a real, in-process host and a real
///     SQLite database (see <see cref="NinlilDnsWebApplicationFactory" />): a real
///     <c>POST /api/backup</c> download, a real <c>POST /api/backup/restore</c> upload that
///     genuinely wipes and re-imports this host's own database, and the rejection paths (a
///     missing/too-short passphrase, a wrong passphrase on restore, a missing archive file, and
///     the unauthenticated-request rejection every other controller already gets covered for).
///     The underlying export/import/encryption engine itself is covered far more thoroughly by
///     <c>Ninlil.Dns.Server.Backup.Tests</c> -- this class exists to prove the REST layer
///     wires that engine up correctly, the same "REST CRUD vs. the real underlying behavior are
///     two separate verification concerns" convention <see cref="DnsRewriteCrudTests" /> already
///     establishes.
/// </summary>
[Collection(WebTestCollection.Name)]
public class BackupCrudTests(NinlilDnsWebApplicationFactory factory)
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

    private static HttpContent RestoreFormContent(byte[] archive, string passphrase)
    {
        MultipartFormDataContent content = new();

        ByteArrayContent archiveContent = new(archive);
        archiveContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(archiveContent, "Archive", "backup.ltbk");
        content.Add(new StringContent(passphrase ?? ""), "Passphrase");

        return content;
    }

    [Fact]
    public async Task Create_WithoutAuthentication_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/backup",
            new BackupCreateRequest("a-strong-test-passphrase")
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithATooShortPassphrase_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/backup",
            new BackupCreateRequest("short")
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithAValidPassphrase_ReturnsADownloadableEncryptedArchive()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/backup",
            new BackupCreateRequest("a-strong-test-passphrase")
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(response.Content.Headers.ContentDisposition);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition.DispositionType);
        Assert.EndsWith(".ltbk", response.Content.Headers.ContentDisposition.FileName.Trim('"'));

        byte[] archive = await response.Content.ReadAsByteArrayAsync();
        Assert.True(archive.Length > 0);
    }

    [Fact]
    public async Task RestoreLifecycle_BackupThenRestore_RoundTripsARealRewrite()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string domain = $"backup-crud-{Guid.NewGuid():N}.example.com";

        // Create a real, distinctive piece of data through the ordinary REST surface, so a
        // successful restore can be confirmed against something genuinely round-tripped, not
        // merely against a non-zero row count.
        HttpResponseMessage createRewriteResponse = await client.PostAsJsonAsync(
            "/api/dns-rewrites",
            new DnsRewriteWriteRequest(domain, "203.0.113.201")
        );
        createRewriteResponse.EnsureSuccessStatusCode();
        DnsRewriteDetail createdRewrite =
            await createRewriteResponse.Content.ReadFromJsonAsync<DnsRewriteDetail>();

        HttpResponseMessage backupResponse = await client.PostAsJsonAsync(
            "/api/backup",
            new BackupCreateRequest("a-strong-test-passphrase")
        );
        backupResponse.EnsureSuccessStatusCode();
        byte[] archive = await backupResponse.Content.ReadAsByteArrayAsync();

        HttpResponseMessage restoreResponse = await client.PostAsync(
            "/api/backup/restore",
            RestoreFormContent(archive, "a-strong-test-passphrase")
        );

        Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);
        BackupRestoreResponse summary =
            await restoreResponse.Content.ReadFromJsonAsync<BackupRestoreResponse>();
        Assert.True(summary.TotalEntitiesImported > 0);
        Assert.True(summary.EntityCounts["DnsRewrites"] >= 1);
        Assert.Contains("restart", summary.Note, StringComparison.OrdinalIgnoreCase);

        // A restore replaces the underlying database (and, transitively, this host's own bearer
        // token session store), so re-authenticate before reading the rewrite back.
        HttpClient verifyClient = await CreateAuthenticatedClientAsync();
        HttpResponseMessage getRewriteResponse = await verifyClient.GetAsync(
            $"/api/dns-rewrites/{createdRewrite.Id}"
        );
        getRewriteResponse.EnsureSuccessStatusCode();
        DnsRewriteDetail restoredRewrite =
            await getRewriteResponse.Content.ReadFromJsonAsync<DnsRewriteDetail>();
        Assert.Equal(domain, restoredRewrite.Domain);
        Assert.Equal("203.0.113.201", restoredRewrite.Target);
    }

    [Fact]
    public async Task Restore_WithTheWrongPassphrase_ReturnsBadRequestAndLeavesDataUntouched()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string domain = $"backup-crud-wrong-pass-{Guid.NewGuid():N}.example.com";

        HttpResponseMessage createRewriteResponse = await client.PostAsJsonAsync(
            "/api/dns-rewrites",
            new DnsRewriteWriteRequest(domain, "203.0.113.202")
        );
        createRewriteResponse.EnsureSuccessStatusCode();
        DnsRewriteDetail createdRewrite =
            await createRewriteResponse.Content.ReadFromJsonAsync<DnsRewriteDetail>();

        HttpResponseMessage backupResponse = await client.PostAsJsonAsync(
            "/api/backup",
            new BackupCreateRequest("the-real-passphrase-12345")
        );
        backupResponse.EnsureSuccessStatusCode();
        byte[] archive = await backupResponse.Content.ReadAsByteArrayAsync();

        HttpResponseMessage restoreResponse = await client.PostAsync(
            "/api/backup/restore",
            RestoreFormContent(archive, "a-completely-different-passphrase")
        );

        Assert.Equal(HttpStatusCode.BadRequest, restoreResponse.StatusCode);

        // The rejected restore must never have touched this host's own real database -- the
        // rewrite created immediately above is still there, unaffected.
        HttpResponseMessage getRewriteResponse = await client.GetAsync(
            $"/api/dns-rewrites/{createdRewrite.Id}"
        );
        Assert.Equal(HttpStatusCode.OK, getRewriteResponse.StatusCode);
    }

    [Fact]
    public async Task RestoreLifecycle_BackupWithNoPassphraseThenRestoreWithNone_RoundTripsARealRewrite()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string domain = $"backup-crud-unencrypted-{Guid.NewGuid():N}.example.com";

        HttpResponseMessage createRewriteResponse = await client.PostAsJsonAsync(
            "/api/dns-rewrites",
            new DnsRewriteWriteRequest(domain, "203.0.113.203")
        );
        createRewriteResponse.EnsureSuccessStatusCode();
        DnsRewriteDetail createdRewrite =
            await createRewriteResponse.Content.ReadFromJsonAsync<DnsRewriteDetail>();

        // No passphrase supplied at all -- a real operator choice to skip encryption, expected
        // to still succeed and produce a downloadable archive.
        HttpResponseMessage backupResponse = await client.PostAsJsonAsync(
            "/api/backup",
            new BackupCreateRequest(null)
        );
        backupResponse.EnsureSuccessStatusCode();
        byte[] archive = await backupResponse.Content.ReadAsByteArrayAsync();

        HttpResponseMessage restoreResponse = await client.PostAsync(
            "/api/backup/restore",
            RestoreFormContent(archive, null)
        );

        Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);
        BackupRestoreResponse summary =
            await restoreResponse.Content.ReadFromJsonAsync<BackupRestoreResponse>();
        Assert.True(summary.TotalEntitiesImported > 0);

        HttpClient verifyClient = await CreateAuthenticatedClientAsync();
        HttpResponseMessage getRewriteResponse = await verifyClient.GetAsync(
            $"/api/dns-rewrites/{createdRewrite.Id}"
        );
        getRewriteResponse.EnsureSuccessStatusCode();
        DnsRewriteDetail restoredRewrite =
            await getRewriteResponse.Content.ReadFromJsonAsync<DnsRewriteDetail>();
        Assert.Equal(domain, restoredRewrite.Domain);
    }

    [Fact]
    public async Task Restore_OfAnEncryptedArchiveWithNoPassphrase_ReturnsBadRequestAndLeavesDataUntouched()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string domain = $"backup-crud-missing-pass-{Guid.NewGuid():N}.example.com";

        HttpResponseMessage createRewriteResponse = await client.PostAsJsonAsync(
            "/api/dns-rewrites",
            new DnsRewriteWriteRequest(domain, "203.0.113.204")
        );
        createRewriteResponse.EnsureSuccessStatusCode();
        DnsRewriteDetail createdRewrite =
            await createRewriteResponse.Content.ReadFromJsonAsync<DnsRewriteDetail>();

        HttpResponseMessage backupResponse = await client.PostAsJsonAsync(
            "/api/backup",
            new BackupCreateRequest("the-real-passphrase-12345")
        );
        backupResponse.EnsureSuccessStatusCode();
        byte[] archive = await backupResponse.Content.ReadAsByteArrayAsync();

        HttpResponseMessage restoreResponse = await client.PostAsync(
            "/api/backup/restore",
            RestoreFormContent(archive, null)
        );

        Assert.Equal(HttpStatusCode.BadRequest, restoreResponse.StatusCode);

        HttpResponseMessage getRewriteResponse = await client.GetAsync(
            $"/api/dns-rewrites/{createdRewrite.Id}"
        );
        Assert.Equal(HttpStatusCode.OK, getRewriteResponse.StatusCode);
    }

    [Fact]
    public async Task Restore_WithoutAnArchiveFile_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        MultipartFormDataContent content = new();
        content.Add(new StringContent("a-strong-test-passphrase"), "Passphrase");

        HttpResponseMessage response = await client.PostAsync("/api/backup/restore", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Restore_WithoutAuthentication_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsync(
            "/api/backup/restore",
            RestoreFormContent([1, 2, 3], "a-strong-test-passphrase")
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
