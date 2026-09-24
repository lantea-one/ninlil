using System.Net;

namespace Ninlil.Dns.Server.SelfUpdate.Tests.Fakes;

/// <summary>
///     This class stands in for a real outbound HTTP transport in a test, answering every request
///     via a caller-supplied delegate -- so a test can prove exactly what
///     <see cref="SelfUpdateCheckService" /> does with a given release-feed response (a real
///     document, malformed JSON, or a transport failure) without ever making a real network call.
/// </summary>
/// <param name="respond">
///     The delegate invoked for every request this handler receives; also records whether it was
///     ever invoked at all, so a test can prove a request was (or, deliberately, was not) made.
/// </param>
public sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    : HttpMessageHandler
{
    /// <summary>
    ///     This property contains how many requests this handler has actually answered -- used by
    ///     a test to prove a check against an unconfigured feed URL never makes a request at all.
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
    ///     This static method builds a handler that always throws, standing in for a completely
    ///     unreachable feed URL -- used to prove a skipped check (an empty
    ///     <see cref="SelfUpdateOptions.ReleaseFeedUrl" />) never even attempts a request.
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
    ///     code and no body, standing in for a real server-side failure.
    /// </summary>
    public static FakeHttpMessageHandler ReturningStatus(HttpStatusCode statusCode) =>
        new(_ => new HttpResponseMessage(statusCode));
}
