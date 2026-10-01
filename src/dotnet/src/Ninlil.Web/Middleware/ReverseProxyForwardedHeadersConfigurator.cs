using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Middleware;

/// <summary>
///     This class fills the <see cref="ForwardedHeadersOptions" /> that
///     <see cref="ReverseProxyTrustGatingMiddleware" /> and
///     <see cref="Microsoft.AspNetCore.Builder.ForwardedHeadersExtensions.UseForwardedHeaders(Microsoft.AspNetCore.Builder.IApplicationBuilder)" />
///     share from <see cref="ReverseProxyOptions" />. It lives outside <c>Program.cs</c> so a test
///     can call it directly and guard the loopback rule described on <see cref="Configure" />.
/// </summary>
public static class ReverseProxyForwardedHeadersConfigurator
{
    /// <summary>
    ///     This method sets <paramref name="forwardedHeadersOptions" />'s
    ///     <see cref="ForwardedHeadersOptions.ForwardedHeaders" />,
    ///     <see cref="ForwardedHeadersOptions.KnownProxies" /> and
    ///     <see cref="ForwardedHeadersOptions.KnownIPNetworks" /> from
    ///     <paramref name="reverseProxyOptions" />. Malformed entries are logged and skipped.
    ///     <para>
    ///         <b>A new <see cref="ForwardedHeadersOptions" /> already trusts loopback:</b> its
    ///         constructor puts <c>::1</c> in <see cref="ForwardedHeadersOptions.KnownProxies" />
    ///         and <c>127.0.0.0/8</c> in <see cref="ForwardedHeadersOptions.KnownIPNetworks" />.
    ///         Adding the configured entries on top would leave loopback always trusted, whatever
    ///         the operator configured, so both collections are cleared first and the configured
    ///         allowlist is the whole trust boundary.
    ///     </para>
    /// </summary>
    /// <param name="forwardedHeadersOptions">The instance to fill.</param>
    /// <param name="reverseProxyOptions">The configured allowlist.</param>
    /// <param name="logger">Warns about a malformed allowlist entry.</param>
    public static void Configure(
        ForwardedHeadersOptions forwardedHeadersOptions,
        ReverseProxyOptions reverseProxyOptions,
        ILogger logger
    )
    {
        forwardedHeadersOptions.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        forwardedHeadersOptions.KnownProxies.Clear();
        forwardedHeadersOptions.KnownIPNetworks.Clear();

        foreach (string proxy in reverseProxyOptions.KnownProxies)
        {
            if (System.Net.IPAddress.TryParse(proxy, out System.Net.IPAddress address))
                forwardedHeadersOptions.KnownProxies.Add(address);
            else
                logger.LogWarning(
                    "Ignoring malformed reverse-proxy KnownProxies entry '{Entry}' -- not a valid IP address.",
                    proxy
                );
        }

        foreach (string network in reverseProxyOptions.KnownNetworks)
        {
            if (System.Net.IPNetwork.TryParse(network, out System.Net.IPNetwork parsedNetwork))
                forwardedHeadersOptions.KnownIPNetworks.Add(parsedNetwork);
            else
                logger.LogWarning(
                    "Ignoring malformed reverse-proxy KnownNetworks entry '{Entry}' -- not a valid CIDR network.",
                    network
                );
        }
    }
}
