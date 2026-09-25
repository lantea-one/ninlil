using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate.Tests.Fakes;

/// <summary>
///     This class replaces the HTTP transport in tests. It answers every request with a
///     caller-supplied delegate and counts requests, so a test can check what
///     <see cref="SelfUpdateCheckService" /> does with a given feed response (a valid document,
///     malformed JSON, or an error status) without network access.
/// </summary>
/// <param name="respond">
///     The delegate that builds the response to each request.
/// </param>
public sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    : HttpMessageHandler
{
    /// <summary>
    ///     This property contains how many requests the handler has received, so a test can check
    ///     that no request was made (for example with no feed URL configured).
    /// </summary>
    public int RequestCount { get; private set; }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        RequestCount++;
        return Task.FromResult(respond(request));
    }

    /// <summary>
    ///     This static method builds a handler that always throws, like an unreachable host. The
    ///     tests use it where no request should be made at all.
    /// </summary>
    public static FakeHttpMessageHandler Unreachable() =>
        new(_ => throw new HttpRequestException("Simulated unreachable host."));

    /// <summary>
    ///     This static method builds a handler that always answers with the given raw JSON body
    ///     and a <c>200 OK</c> status.
    /// </summary>
    public static FakeHttpMessageHandler Returning(string jsonBody) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json"),
        });

    /// <summary>
    ///     This static method builds a handler that always answers with the given HTTP status
    ///     code and no body, like a failing feed server.
    /// </summary>
    public static FakeHttpMessageHandler ReturningStatus(HttpStatusCode statusCode) =>
        new(_ => new HttpResponseMessage(statusCode));
}
