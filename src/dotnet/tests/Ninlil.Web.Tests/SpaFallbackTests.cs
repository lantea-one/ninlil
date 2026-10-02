using System.Net;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the SPA fallback route (<c>MapFallbackToFile</c> in <c>Program.cs</c>)
///     over HTTP on the shared in-process host, which serves the <c>wwwroot/</c> of the build
///     output. A client-side deep link must get <c>index.html</c>, while an unmatched path under a
///     reserved prefix such as <c>/api</c> and a request for a missing static asset (such as a
///     stale content-hashed bundle) must get 404 rather than <c>index.html</c> served as
///     <c>text/html</c>. An existing static file must be served directly.
/// </summary>
[Collection(WebTestCollection.Name)]
public class SpaFallbackTests(NinlilDnsWebApplicationFactory factory)
{
    [Fact]
    public async Task Fallback_ForADeepLinkClientRoute_ServesIndexHtml()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/zones/123");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/html", response.Content.Headers.ContentType?.MediaType);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("<html", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fallback_ForAnUnmatchedApiRoute_Returns404NotIndexHtml()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/totally-not-a-real-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Fallback_ForAMissingStaticLookingAsset_Returns404NotIndexHtml()
    {
        HttpClient client = factory.CreateClient();

        // A content-hashed bundle chunk that does not exist under wwwroot/, as a stale cached
        // index.html requests after a rebuild. It must get a 404: serving index.html with
        // text/html instead breaks module loading in the browser.
        HttpResponseMessage response = await client.GetAsync("/chunk-DOESNOTEXIST.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Fallback_ForARealStaticAsset_ServesItDirectlyNotIndexHtml()
    {
        HttpClient client = factory.CreateClient();

        // favicon.ico is part of the Angular build output and must be served by
        // UseStaticFiles() without reaching the fallback route.
        HttpResponseMessage response = await client.GetAsync("/favicon.ico");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}
