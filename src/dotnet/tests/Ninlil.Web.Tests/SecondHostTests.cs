using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class checks that a second in-process host can run next to the shared one. It uses
///     <see cref="NinlilDnsWebApplicationFactory.CreateSecondHost" /> on the shared factory and
///     real in-memory requests against both hosts.
/// </summary>
[Collection(WebTestCollection.Name)]
public sealed class SecondHostTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This test proves that a second host answers a request, that the shared host still
    ///     answers after the second one is disposed, and that the two hosts hold different
    ///     service providers. A second host that shares the first host's listener ports stops
    ///     itself at start-up and answers 500 with an <see cref="ObjectDisposedException" />.
    /// </summary>
    [Fact]
    public async Task CreateSecondHost_NextToTheSharedHost_BothAnswerRequests()
    {
        using WebApplicationFactory<Program> second = factory.CreateSecondHost(builder =>
            builder.ConfigureTestServices(_ => { })
        );
        Assert.NotSame(factory.Services, second.Services);

        using (HttpClient secondClient = second.CreateClient())
        {
            HttpResponseMessage response = await secondClient.GetAsync("/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        second.Dispose();

        using HttpClient sharedClient = factory.CreateClient();
        HttpResponseMessage sharedResponse = await sharedClient.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, sharedResponse.StatusCode);
    }
}
