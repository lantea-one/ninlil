using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Middleware;

/// <summary>
///     This middleware decides, for each request, whether the connecting peer is a trusted reverse
///     proxy allowed to report a different client address. It runs just before
///     <see cref="Microsoft.AspNetCore.Builder.ForwardedHeadersExtensions.UseForwardedHeaders(IApplicationBuilder)" />
///     and either leaves the <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> headers for that
///     middleware to apply, or removes both before calling the next delegate. An untrusted request
///     is not rejected; it is answered with the connection's own peer address and scheme, as if it
///     had sent no forwarded headers. <c>Program.cs</c> adds it only when
///     <see cref="ReverseProxyOptions.Enable" /> is set.
///     <para>
///         Trust needs two checks to pass: the peer address must match
///         <see cref="ForwardedHeadersOptions.KnownProxies" /> or
///         <see cref="ForwardedHeadersOptions.KnownIPNetworks" />, and, when
///         <see cref="ReverseProxyOptions.SharedSecret" /> is set, the configured header must carry
///         that exact value. The address allowlist says which upstream may report a client
///         address; the optional secret adds defense in depth against a request that comes
///         straight from an address inside an allowlisted network without passing through the
///         proxy.
///     </para>
///     <para>
///         <see cref="Microsoft.AspNetCore.HttpOverrides.ForwardedHeadersMiddleware" /> trusts
///         every peer when its allowlist is empty. Running this middleware first, and always
///         removing the headers for a peer it does not recognize, means that fallback is never
///         reached (see <see cref="IsKnownProxySource" />).
///     </para>
/// </summary>
/// <param name="next">The next delegate in the pipeline.</param>
/// <param name="forwardedHeadersOptions">
///     The allowlist <see cref="Microsoft.AspNetCore.HttpOverrides.ForwardedHeadersMiddleware" />
///     was configured with, reused rather than parsed again from
///     <see cref="ReverseProxyOptions" />, so the two middlewares cannot disagree.
/// </param>
/// <param name="reverseProxyOptions">The reverse-proxy trust settings loaded at startup.</param>
public sealed class ReverseProxyTrustGatingMiddleware(
    RequestDelegate next,
    IOptions<ForwardedHeadersOptions> forwardedHeadersOptions,
    IOptions<ReverseProxyOptions> reverseProxyOptions
)
{
    /// <summary>
    ///     This constant contains the de facto header name reverse proxies use for the forwarded
    ///     client address (RFC 7239's <c>Forwarded</c> header is not used).
    /// </summary>
    private const string ForwardedForHeaderName = "X-Forwarded-For";

    /// <summary>
    ///     This constant contains the header name reverse proxies use for the original request
    ///     scheme.
    /// </summary>
    private const string ForwardedProtoHeaderName = "X-Forwarded-Proto";

    /// <summary>
    ///     This method is the middleware entry point, called once per request.
    /// </summary>
    public Task InvokeAsync(HttpContext context)
    {
        // For a peer that is not a trusted proxy, the forwarded headers are removed before
        // ForwardedHeadersMiddleware sees them, so it leaves the peer address and scheme alone. The
        // request is still answered; its claims about its origin are just not believed.
        if (!IsTrustedRequest(context))
        {
            context.Request.Headers.Remove(ForwardedForHeaderName);
            context.Request.Headers.Remove(ForwardedProtoHeaderName);
        }

        return next(context);
    }

    /// <summary>
    ///     This method decides whether the connecting peer is a known proxy and, when a shared
    ///     secret is set, sent the right value for it.
    /// </summary>
    private bool IsTrustedRequest(HttpContext context)
    {
        IPAddress remoteAddress = context.Connection.RemoteIpAddress;

        if (remoteAddress is null || !IsKnownProxySource(remoteAddress))
            return false;

        string configuredSecret = reverseProxyOptions.Value.SharedSecret;

        // Without a configured secret the allowlist alone decides, and the peer already matched it.
        if (string.IsNullOrEmpty(configuredSecret))
            return true;

        string headerName = string.IsNullOrWhiteSpace(
            reverseProxyOptions.Value.SharedSecretHeaderName
        )
            ? ReverseProxyOptions.DefaultSharedSecretHeaderName
            : reverseProxyOptions.Value.SharedSecretHeaderName;

        if (!context.Request.Headers.TryGetValue(headerName, out StringValues headerValues))
            return false;

        string presentedSecret = headerValues.Count > 0 ? headerValues[0] : null;

        return SecretsMatch(configuredSecret, presentedSecret);
    }

    /// <summary>
    ///     This method checks the peer address against
    ///     <see cref="ForwardedHeadersOptions.KnownProxies" /> (exact match) and
    ///     <see cref="ForwardedHeadersOptions.KnownIPNetworks" /> (CIDR containment). Kestrel's
    ///     IPv6 socket also accepts IPv4 connections in mapped form (<c>::ffff:a.b.c.d</c>), so the
    ///     unmapped IPv4 address is checked too, and a plain IPv4 proxy address or network still
    ///     matches.
    /// </summary>
    private bool IsKnownProxySource(IPAddress remoteAddress)
    {
        ForwardedHeadersOptions options = forwardedHeadersOptions.Value;
        IPAddress mappedAddress = remoteAddress.IsIPv4MappedToIPv6
            ? remoteAddress.MapToIPv4()
            : null;

        foreach (IPAddress knownProxy in options.KnownProxies)
        {
            if (
                knownProxy.Equals(remoteAddress)
                || (mappedAddress is not null && knownProxy.Equals(mappedAddress))
            )
                return true;
        }

        foreach (IPNetwork knownNetwork in options.KnownIPNetworks)
        {
            if (
                knownNetwork.Contains(remoteAddress)
                || (mappedAddress is not null && knownNetwork.Contains(mappedAddress))
            )
                return true;
        }

        return false;
    }

    /// <summary>
    ///     This method compares the presented secret with the configured one in constant time with
    ///     <see cref="CryptographicOperations.FixedTimeEquals" />, as <c>TsigService</c> compares a
    ///     TSIG MAC, so response timing does not reveal how much of a guess was right.
    /// </summary>
    private static bool SecretsMatch(string configuredSecret, string presentedSecret)
    {
        if (string.IsNullOrEmpty(presentedSecret))
            return false;

        byte[] expected = Encoding.UTF8.GetBytes(configuredSecret);
        byte[] actual = Encoding.UTF8.GetBytes(presentedSecret);

        return expected.Length == actual.Length
            && CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
