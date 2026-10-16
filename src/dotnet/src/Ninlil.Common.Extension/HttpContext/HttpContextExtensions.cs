using System.Net;
using System.Net.Sockets;
using Microsoft.Net.Http.Headers;

// Define our namespace.
namespace Ninlil.Common.Extension.HttpContext;

/// <summary>
///     This static class reads client details (address, bearer token, user agent) from an ASP.NET
///     Core <see cref="Microsoft.AspNetCore.Http.HttpContext" />. The address comes from the connection
///     only, never from a forwarded header.
/// </summary>
public static class HttpContextExtensions
{
    /// <summary>
    ///     This method returns the address of the peer that opened the connection, in canonical
    ///     form: an IPv4-mapped IPv6 address becomes IPv4, an IPv6 address loses its scope id and
    ///     any other address is unchanged. It returns <see langword="null" /> when the connection
    ///     has no remote address. Behind a reverse proxy, configure the forwarded-headers
    ///     middleware with the proxies' addresses (<c>Ninlil.Web</c> does this from
    ///     <c>ReverseProxy:KnownProxies</c> and <c>ReverseProxy:KnownNetworks</c>); it replaces the
    ///     remote address for trusted peers only, and this method then returns the client's
    ///     address.
    /// </summary>
    /// <param name="instance">The HttpContext instance to pull the IP address from.</param>
    /// <returns>The IP address of the current client, or <see langword="null" />.</returns>
    public static string GetIpAddress(this Microsoft.AspNetCore.Http.HttpContext instance)
    {
        IPAddress remote = instance.Connection.RemoteIpAddress;
        if (remote is null)
            return null;

        // Security decision: this method never reads X-Forwarded-For or any other forwarded
        // header. Those headers are client-controlled unless the immediate peer is a trusted
        // proxy, and this library has no trust list. Proxy trust lives in the forwarded-headers
        // middleware (Program.cs in Ninlil.Web, gated by ReverseProxyTrustGatingMiddleware),
        // which rewrites RemoteIpAddress for trusted peers only.
        if (remote.IsIPv4MappedToIPv6)
            return remote.MapToIPv4().ToString();

        // A scope id (fe80::1%2) names a local interface, not part of the client's identity.
        if (remote.AddressFamily == AddressFamily.InterNetworkV6 && remote.ScopeId != 0)
            return new IPAddress(remote.GetAddressBytes()).ToString();

        return remote.ToString();
    }

    /// <summary>
    ///     This method returns the token from an <c>Authorization: Bearer ...</c> header, or
    ///     <see langword="null" /> when there is no bearer header. The scheme matches
    ///     case-insensitively and must be followed by at least one space (RFC 6750 §2.1, RFC 9110
    ///     §11.1), so <c>BearerXYZ</c> has no token. A header without a token gives
    ///     <see langword="null" />.
    /// </summary>
    /// <param name="instance">The HttpContext instance to pull the authorization bearer from.</param>
    /// <returns>The authorization bearer from the HTTP request <paramref name="instance" /> if it exists.</returns>
    public static string GetAuthorizationBearer(this Microsoft.AspNetCore.Http.HttpContext instance)
    {
        // Make sure we have an authorization header and send the result.
        if (!instance.Request.Headers.ContainsKey(HeaderNames.Authorization))
            return null;

        // Localize the authorization header value.
        string authorizationHeader = instance
            .Request.Headers.Where(h => h.Key.ToLower().Equals(HeaderNames.Authorization.ToLower()))
            .Select(h => h.Value)
            .FirstOrDefault();

        // A blank header has no token.
        if (authorizationHeader?.Trim() is null or "")
            return null;

        // The scheme and the token are separated by at least one space.
        const string Scheme = "bearer ";
        if (!authorizationHeader.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
            return null;

        string token = authorizationHeader[Scheme.Length..].Trim();
        return token.Length == 0 ? null : token;
    }

    /// <summary>
    ///     This method returns the requesting client's browser string from the HTTP request <paramref name="instance" />.
    /// </summary>
    /// <param name="instance">The HttpContext instance to pull the client's browser string from.</param>
    /// <returns>The client's browser string.</returns>
    public static string GetUserAgent(this Microsoft.AspNetCore.Http.HttpContext instance) =>
        instance.Request.Headers[HeaderNames.UserAgent].ToString();
}
