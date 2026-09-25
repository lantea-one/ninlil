// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate.Tests.Fakes;

/// <summary>
///     This class is an <see cref="IHttpClientFactory" /> for tests: every
///     <see cref="CreateClient" /> call, whatever the name, returns a new
///     <see cref="HttpClient" /> over the same <see cref="FakeHttpMessageHandler" />.
/// </summary>
/// <param name="handler">The handler every returned client is wired to.</param>
public sealed class SingleHandlerHttpClientFactory(FakeHttpMessageHandler handler)
    : IHttpClientFactory
{
    /// <inheritdoc />
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
