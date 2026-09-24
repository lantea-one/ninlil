namespace Ninlil.Dns.Server.SelfUpdate.Tests.Fakes;

/// <summary>
///     This class stands in for a real, DI-built <see cref="IHttpClientFactory" />: every call to
///     <see cref="CreateClient" />, regardless of the requested client name, returns a fresh
///     <see cref="HttpClient" /> wrapping the same caller-supplied <see cref="FakeHttpMessageHandler" />
///     -- which is all <see cref="SelfUpdateCheckService" />'s own single named client needs in a
///     unit test.
/// </summary>
/// <param name="handler">The handler every returned client is wired to.</param>
public sealed class SingleHandlerHttpClientFactory(FakeHttpMessageHandler handler)
    : IHttpClientFactory
{
    /// <inheritdoc />
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
