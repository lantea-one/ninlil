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
///     This middleware decides, for every incoming request, whether the connecting peer is a
///     genuinely trusted reverse proxy allowed to claim a different client address on this
///     request's behalf. It runs immediately before <see cref="Microsoft.AspNetCore.Builder.ForwardedHeadersExtensions.UseForwardedHeaders(IApplicationBuilder)" />
///     in the pipeline and either lets a request's own <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c>
///     headers through unchanged (so that middleware can process them) or strips both headers
///     entirely before <see cref="InvokeAsync" /> ever calls the next delegate -- so an untrusted
///     request is always answered using the raw connection's own peer address, exactly as if it
///     had sent no forwarded headers at all, rather than being rejected outright.
///     <para>
///         Trust requires two independent checks to both pass: the connecting peer's own address
///         must match one of <see cref="ForwardedHeadersOptions.KnownProxies" />/
///         <see cref="ForwardedHeadersOptions.KnownIPNetworks" />, and, when
///         <see cref="ReverseProxyOptions.SharedSecret" /> is configured, the configured header
///         must also carry the exact matching value. The address allowlist alone establishes
///         which upstream is even allowed to claim a forwarded address; the optional shared
///         secret is real defense-in-depth on top of it, protecting against a request that
///         reaches this host directly from an address that happens to sit inside an allowlisted
///         CIDR block without having actually passed through the real, correctly-configured
///         proxy.
///     </para>
///     <para>
///         This project deliberately does not rely on
///         <see cref="Microsoft.AspNetCore.HttpOverrides.ForwardedHeadersMiddleware" />'s own
///         built-in "trust every peer when the allowlist is empty" fallback behavior --
///         registering this middleware ahead of it, and always stripping the forwarded headers
///         for a peer this middleware itself does not recognize, keeps that fallback from ever
///         being reachable in practice (see <see cref="IsKnownProxySource" />).
///     </para>
/// </summary>
/// <param name="next">The next delegate in the pipeline.</param>
/// <param name="forwardedHeadersOptions">
///     The same allowlist <see cref="Microsoft.AspNetCore.HttpOverrides.ForwardedHeadersMiddleware" />
///     itself was configured with --
///     reused directly, rather than parsed a second time from <see cref="ReverseProxyOptions" />,
///     so the two middlewares can never drift out of sync with each other.
/// </param>
/// <param name="reverseProxyOptions">This host's own currently-effective reverse-proxy trust settings.</param>
public sealed class ReverseProxyTrustGatingMiddleware(
    RequestDelegate next,
    IOptions<ForwardedHeadersOptions> forwardedHeadersOptions,
    IOptions<ReverseProxyOptions> reverseProxyOptions
)
{
    /// <summary>
    ///     This constant contains the header name RFC 7239's own conventional predecessor,
    ///     and every major reverse proxy, still actually uses on the wire for the forwarded
    ///     client address.
    /// </summary>
    private const string ForwardedForHeaderName = "X-Forwarded-For";

    /// <summary>
    ///     This constant contains the header name every major reverse proxy uses on the wire for
    ///     the forwarded original request scheme.
    /// </summary>
    private const string ForwardedProtoHeaderName = "X-Forwarded-Proto";

    /// <summary>
    ///     This method is the actual middleware entry point, invoked once per request.
    /// </summary>
    public Task InvokeAsync(HttpContext context)
    {
        // A request whose own connecting peer is not a genuinely trusted proxy has its forwarded
        // headers stripped before the next middleware (ForwardedHeadersMiddleware) ever sees
        // them, so it has nothing to process and simply leaves the connection's own real peer
        // address and scheme untouched -- the request is still answered normally, just without
        // ever trusting whatever it claimed about its own origin.
        if (!IsTrustedRequest(context))
        {
            context.Request.Headers.Remove(ForwardedForHeaderName);
            context.Request.Headers.Remove(ForwardedProtoHeaderName);
        }

        return next(context);
    }

    /// <summary>
    ///     This method decides whether the current request's own connecting peer is both a known
    ///     proxy and, when a shared secret is configured, carrying the correct value for it.
    /// </summary>
    private bool IsTrustedRequest(HttpContext context)
    {
        IPAddress remoteAddress = context.Connection.RemoteIpAddress;

        if (remoteAddress is null || !IsKnownProxySource(remoteAddress))
            return false;

        string configuredSecret = reverseProxyOptions.Value.SharedSecret;

        // An unconfigured secret means the allowlist alone is the trust boundary -- nothing
        // further to check once the connecting peer is already a known proxy.
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
    ///     This method checks a connecting peer's own address against both
    ///     <see cref="ForwardedHeadersOptions.KnownProxies" /> (an exact address match) and
    ///     <see cref="ForwardedHeadersOptions.KnownIPNetworks" /> (a CIDR containment check) --
    ///     comparing both the address as reported and, since this host's Kestrel listener binds
    ///     an IPv6 socket that also accepts IPv4 connections mapped into IPv6 form
    ///     (<c>::ffff:a.b.c.d</c>), its own unmapped IPv4 equivalent when one exists, so an
    ///     operator configuring a plain IPv4 proxy address or network still matches regardless of
    ///     which form the connection actually arrived in.
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
    ///     This method compares a presented secret against the configured one in constant time,
    ///     the same <see cref="CryptographicOperations.FixedTimeEquals" />-based approach
    ///     <c>TsigService</c> already uses to compare a TSIG MAC, so a timing side-channel can
    ///     never leak how many leading bytes of a guessed secret were correct.
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
