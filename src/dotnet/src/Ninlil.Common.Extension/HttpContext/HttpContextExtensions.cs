using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

// Define our namespace.
namespace Ninlil.Common.Extension.HttpContext;

/// <summary>
///     This static class reads client details (address, bearer token, user agent) from an ASP.NET
///     Core <see cref="Microsoft.AspNetCore.Http.HttpContext" />.
/// </summary>
public static class HttpContextExtensions
{
    /// <summary>
    ///     This method returns the client's IP address: the first address in an
    ///     <c>X-Forwarded-For</c> header when one is present, otherwise the connection's remote
    ///     address. The header is trusted as sent, so only use this behind a proxy that sets it.
    /// </summary>
    /// <param name="instance">The HttpContext instance to pull the IP address from.</param>
    /// <returns>The IP address of the current client.</returns>
    public static string GetIpAddress(this Microsoft.AspNetCore.Http.HttpContext instance)
    {
        // For a proxied request, return the first (original caller's) address in the forwarded
        // chain. A missing or blank header falls through to RemoteIpAddress below.
        if (
            instance.Request.Headers.TryGetValue("x-forwarded-for", out StringValues forwardedFor)
            && !string.IsNullOrWhiteSpace(forwardedFor.ToString())
        )
        {
            return forwardedFor.ToString().Split(',')[0].Trim();
        }

        // We're done, return the IP address from the framework.
        return instance.Connection.RemoteIpAddress?.MapToIPv4().ToString()
            ?? instance.Connection.RemoteIpAddress?.MapToIPv6().ToString();
    }

    /// <summary>
    ///     This method returns the token from an <c>Authorization: Bearer ...</c> header, or
    ///     <see langword="null" /> when there is no bearer header.
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

        // We're done, make sure we have an authorization bearer value and return.
        return !authorizationHeader.StartsWith("bearer", StringComparison.OrdinalIgnoreCase)
            ? null
            : authorizationHeader["bearer".Length..].Trim();
    }

    /// <summary>
    ///     This method returns the requesting client's browser string from the HTTP request <paramref name="instance" />.
    /// </summary>
    /// <param name="instance">The HttpContext instance to pull the client's browser string from.</param>
    /// <returns>The client's browser string.</returns>
    public static string GetUserAgent(this Microsoft.AspNetCore.Http.HttpContext instance) =>
        instance.Request.Headers[HeaderNames.UserAgent].ToString();
}
