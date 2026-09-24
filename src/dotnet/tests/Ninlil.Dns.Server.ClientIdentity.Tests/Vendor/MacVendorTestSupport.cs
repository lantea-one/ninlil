using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.ClientIdentity.Vendor;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests.Vendor;

/// <summary>
///     This class provides the shared fakes the vendor tests need: a mutable options monitor, an
///     HTTP client factory backed by a scripted handler, and synthetic registry CSV content.
/// </summary>
internal static class MacVendorTestSupport
{
    /// <summary>
    ///     This method builds a registry CSV in IEEE's own format with <paramref name="count" />
    ///     sequential prefixes starting at <paramref name="firstPrefix" />.
    /// </summary>
    public static string BuildRegistryCsv(
        MacVendorRegistryEnum registry,
        int count,
        ulong firstPrefix,
        string organization
    )
    {
        StringBuilder builder = new(
            "Registry,Assignment,Organization Name,Organization Address\r\n"
        );
        for (int index = 0; index < count; index++)
        {
            string prefix = (firstPrefix + (ulong)index).ToString("X" + registry.PrefixHexDigits);
            builder.Append(
                $"{registry.IeeeName},{prefix},\"{organization}, Ltd.\",Somewhere 1\r\n"
            );
        }

        return builder.ToString();
    }

    /// <summary>
    ///     This class is an <see cref="IOptionsMonitor{TOptions}" /> whose value a test sets directly.
    /// </summary>
    public sealed class MutableOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
    {
        /// <inheritdoc />
        public TOptions CurrentValue { get; set; } = value;

        /// <inheritdoc />
        public TOptions Get(string name) => CurrentValue;

        /// <inheritdoc />
        public IDisposable OnChange(Action<TOptions, string> listener) => null;
    }

    /// <summary>
    ///     This class is an HTTP handler answering every request through a test-supplied function
    ///     and recording each request it saw.
    /// </summary>
    public sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        /// <summary>Every request received, in order.</summary>
        public List<HttpRequestMessage> Requests { get; } = [];

        /// <summary>The function producing each response; replaceable between refreshes.</summary>
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = respond;

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return Task.FromResult(Respond(request));
        }
    }

    /// <summary>
    ///     This class is an <see cref="IHttpClientFactory" /> handing out clients over one handler.
    /// </summary>
    public sealed class HandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        /// <inheritdoc />
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>
    ///     This method builds a <c>200 OK</c> response carrying CSV content and cache validators.
    /// </summary>
    public static HttpResponseMessage Csv(string content, string etag)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/octet-stream"),
        };
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
        response.Content.Headers.LastModified = DateTimeOffset.Parse("2026-09-23T16:00:00Z");
        return response;
    }
}
