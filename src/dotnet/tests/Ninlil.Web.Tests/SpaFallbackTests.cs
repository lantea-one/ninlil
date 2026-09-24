using System.Net;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <c>Program.cs</c>'s own SPA-fallback <c>MapFallbackToFile</c> route end
///     to end, against a real, in-process host serving this project's own real, compiled
///     <c>wwwroot/</c> (see <see cref="NinlilDnsWebApplicationFactory" />) -- confirming both
///     the reserved-prefix exclusion (an unmatched request under a reserved top-level path still
///     404s rather than being handed the SPA's own HTML) and the static-asset exclusion (a
///     request that looks like a missing static asset, e.g. a stale content-hashed bundle name,
///     404s too, instead of being silently served <c>index.html</c> with a misleading
///     <c>text/html</c> content type), while a genuine client-side deep link still correctly
///     falls back to <c>index.html</c>.
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

        // This path deliberately looks like a content-hashed Angular bundle chunk that does not
        // actually exist under wwwroot/ -- the exact shape a browser's own stale, cached
        // index.html would request after a rebuild replaced wwwroot/'s real contents. Before the
        // fix this test guards, this request was silently handed index.html (a 200 with a
        // text/html content type) instead of a real 404, which is what actually broke module
        // loading in the browser.
        HttpResponseMessage response = await client.GetAsync("/chunk-DOESNOTEXIST.js");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Fallback_ForARealStaticAsset_ServesItDirectlyNotIndexHtml()
    {
        HttpClient client = factory.CreateClient();

        // favicon.ico ships as part of the Angular build's own static output and should be
        // served by UseStaticFiles() directly, never reaching the fallback route at all.
        HttpResponseMessage response = await client.GetAsync("/favicon.ico");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}
