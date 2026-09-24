using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Middleware;

/// <summary>
///     This class builds the <see cref="ForwardedHeadersOptions" /> instance
///     <see cref="ReverseProxyTrustGatingMiddleware" /> and
///     <see cref="Microsoft.AspNetCore.Builder.ForwardedHeadersExtensions.UseForwardedHeaders(Microsoft.AspNetCore.Builder.IApplicationBuilder)" />
///     share, from a <see cref="ReverseProxyOptions" /> instance, extracted out of <c>Program.cs</c>'s
///     own inline configuration lambda specifically so it can be exercised directly by a
///     permanent test -- see this method's own doc comment for the real, checked-not-assumed bug
///     it exists to prevent a regression of.
/// </summary>
public static class ReverseProxyForwardedHeadersConfigurator
{
    /// <summary>
    ///     This method populates <paramref name="forwardedHeadersOptions" />'s own
    ///     <see cref="ForwardedHeadersOptions.ForwardedHeaders" />/<see cref="ForwardedHeadersOptions.KnownProxies" />/
    ///     <see cref="ForwardedHeadersOptions.KnownIPNetworks" /> from <paramref name="reverseProxyOptions" />.
    ///     <para>
    ///         <b>A brand-new <see cref="ForwardedHeadersOptions" /> instance already pre-trusts
    ///         loopback on its own</b> -- its own default constructor seeds
    ///         <see cref="ForwardedHeadersOptions.KnownProxies" /> with <c>::1</c> and
    ///         <see cref="ForwardedHeadersOptions.KnownIPNetworks" /> with <c>127.0.0.0/8</c>, a
    ///         real, checked-not-assumed fact confirmed against a real running host, not read
    ///         from documentation alone: a first attempt at this wiring that only ever *added*
    ///         this project's own configured entries, without clearing these framework defaults
    ///         first, left this host's own reverse-proxy trust gate always trusting a request
    ///         from loopback regardless of what an operator actually configured -- found only by
    ///         an actual end-to-end request from <c>curl</c> over IPv6 loopback (<c>::1</c>)
    ///         honoring a forwarded header despite a deliberately non-matching allowlist. Both
    ///         collections are cleared here before this project's own configured entries are
    ///         added, so an operator's own explicit allowlist is the entire trust boundary, never
    ///         silently widened by this framework default.
    ///     </para>
    /// </summary>
    /// <param name="forwardedHeadersOptions">The instance to populate.</param>
    /// <param name="reverseProxyOptions">The configured allowlist to populate it from.</param>
    /// <param name="logger">A logger a malformed allowlist entry is warned through.</param>
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
