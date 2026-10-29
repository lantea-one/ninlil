using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the document shape the admin console's API client is generated from, on
///     the shared in-process host with real HTTP. Two groups of tests:
///     <list type="bullet">
///         <item>
///             Document tests read <c>/swagger/v1/swagger.json</c> and check what
///             <c>OperationIdConvention</c> and <c>ClientShapeOperationFilter</c> promise: a unique
///             id on every operation, camel-case query names, JSON-only bodies, no dotted tag.
///         </item>
///         <item>
///             Guard tests send the lower-case spellings the document now publishes (<c>skip</c>,
///             <c>take</c>, <c>archive</c>, <c>passphrase</c>). They also pass on the code before
///             the document changed, because ASP.NET Core binds query and form values
///             case-insensitively; they are guards that renaming the names in the document is safe,
///             not reds.
///         </item>
///     </list>
/// </summary>
[Collection(WebTestCollection.Name)]
public class OpenApiClientShapeTests(NinlilDnsWebApplicationFactory factory)
{
    private async Task<JsonNode> GetDocumentAsync()
    {
        string served = await factory.CreateClient().GetStringAsync("/swagger/v1/swagger.json");
        return JsonNode.Parse(served);
    }

    private static IEnumerable<JsonObject> Operations(JsonNode document) =>
        document["paths"]
            .AsObject()
            .SelectMany(path => path.Value.AsObject())
            .Where(entry => entry.Key != "parameters")
            .Select(entry => entry.Value.AsObject());

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
    public async Task Document_EveryOperation_HasAUniqueOperationId()
    {
        JsonNode document = await GetDocumentAsync();

        List<string> ids = [.. Operations(document).Select(o => (string)o["operationId"])];

        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.False(string.IsNullOrEmpty(id)));
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Contains("apiZonesGet", ids);
    }

    [Fact]
    public async Task Document_QueryParameters_StartWithALowerCaseLetter()
    {
        JsonNode document = await GetDocumentAsync();

        List<string> queryNames =
        [
            .. Operations(document)
                .SelectMany(o => o["parameters"]?.AsArray() ?? [])
                .Where(p => (string)p["in"] == "query")
                .Select(p => (string)p["name"]),
        ];

        Assert.Contains("skip", queryNames);
        Assert.Contains("take", queryNames);
        Assert.All(queryNames, name => Assert.True(char.IsLower(name[0]), name));
    }

    [Fact]
    public async Task Document_RequestBodiesAndResponses_ListJsonOnlyWhereTheyListJson()
    {
        JsonNode document = await GetDocumentAsync();

        List<JsonObject> contents =
        [
            .. Operations(document)
                .SelectMany(o =>
                    new[] { o["requestBody"]?["content"] }.Concat(
                        o["responses"].AsObject().Select(response => response.Value["content"])
                    )
                )
                .Where(content => content is not null)
                .Select(content => content.AsObject()),
        ];

        Assert.NotEmpty(contents);
        Assert.All(
            contents.Where(content => content.ContainsKey("application/json")),
            content => Assert.Single(content)
        );
    }

    [Fact]
    public async Task Document_RestoreForm_UsesLowerCaseFieldAndEncodingNames()
    {
        JsonNode document = await GetDocumentAsync();

        JsonObject form = document["paths"]
            ["/api/backup/restore"]["post"]["requestBody"]["content"]["multipart/form-data"]
            .AsObject();

        Assert.Equal(
            ["archive", "passphrase"],
            form["schema"]["properties"].AsObject().Select(p => p.Key)
        );
        Assert.Equal(["archive", "passphrase"], form["encoding"].AsObject().Select(p => p.Key));
    }

    [Fact]
    public async Task Document_Tags_HaveNoDot()
    {
        JsonNode document = await GetDocumentAsync();

        IEnumerable<string> tags = Operations(document)
            .SelectMany(o => o["tags"].AsArray())
            .Select(tag => (string)tag)
            .Concat(document["tags"].AsArray().Select(tag => (string)tag["name"]))
            .Distinct();

        Assert.All(tags, tag => Assert.DoesNotContain('.', tag));
        Assert.Contains("NinlilWeb", tags);
    }

    [Fact]
    public async Task List_WithLowerCaseSkipAndTake_ReturnsThePageAskedFor()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        for (int i = 0; i < 3; i++)
        {
            HttpResponseMessage create = await client.PostAsJsonAsync(
                "/api/dns-rewrites",
                new DnsRewriteWriteRequest($"paging-{Guid.NewGuid():N}.example.com", "203.0.113.77")
            );
            create.EnsureSuccessStatusCode();
        }

        HttpResponseMessage response = await client.GetAsync("/api/dns-rewrites?skip=1&take=2");

        response.EnsureSuccessStatusCode();
        PagedResult<DnsRewriteDetail> page = await response.Content.ReadFromJsonAsync<
            PagedResult<DnsRewriteDetail>
        >();
        Assert.Equal(1, page.Skip);
        Assert.Equal(2, page.Take);
        Assert.Equal(2, page.Items.Count);
        Assert.True(page.TotalCount >= 3);
    }

    [Fact]
    public async Task Restore_WithLowerCaseFormFields_GetsPastTheArchiveMissingCheck()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        MultipartFormDataContent content = new();
        ByteArrayContent archive = new([1, 2, 3, 4]);
        archive.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(archive, "archive", "backup.ltbk");
        content.Add(new StringContent("some-passphrase"), "passphrase");

        HttpResponseMessage response = await client.PostAsync("/api/backup/restore", content);

        // The bytes are not a backup, so the restore is refused (nothing is wiped). The refusal
        // must come from reading the archive, not from "An archive file is required.", which is
        // what an unbound archive field would produce.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("An archive file is required", body, StringComparison.Ordinal);
    }
}
