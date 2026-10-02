using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the backup and restore endpoints of <see cref="BackupController" /> over
///     HTTP on the shared in-process host, signed in as the bootstrap administrator: downloading
///     an archive from <c>POST /api/backup</c>, uploading it to <c>POST /api/backup/restore</c>
///     (which wipes and re-imports the host's SQLite database, so a DNS rewrite created first must
///     come back), and the rejections (a missing or too-short passphrase, a wrong passphrase, a
///     missing archive, no token). The export, import and encryption are covered in depth by
///     <c>Ninlil.Dns.Server.Backup.Tests</c>; this class checks the HTTP layer around them.
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

        // Create a distinctive rewrite, so the restore is checked against data that round-trips,
        // not just a non-zero row count.
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

        // A restore replaces the whole database, refresh-token sessions included, so sign in
        // again before reading the rewrite back.
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

        // The rejected restore must leave the database alone, so the rewrite is still there.
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

        // No passphrase: the operator may skip encryption, and the backup still succeeds.
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
