using System.Net;
using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Web.Services;

/// <summary>
///     This class is the one check for an address-or-network field that
///     <c>ClientAddress.NetworkContains</c> matches: a bare IPv4 or IPv6 address (matched
///     exactly) or a CIDR network. <c>DnsAccessControlRulesController</c>,
///     <c>ClientGroupNetworksController</c> and <c>DnsClientStaticAddressMappingsController</c>
///     use it, so all three accept the same values and give the same 400 message.
/// </summary>
internal static class NetworkEntryFormat
{
    /// <summary>
    ///     This constant is the 400 message format for a value that is neither an address nor a
    ///     CIDR network. Argument 0 is the rejected value.
    /// </summary>
    public const string InvalidMessageFormat =
        "'{0}' is not a valid IP address or CIDR network (for example 192.0.2.7, 192.0.2.0/24 or 2001:db8::/32).";

    /// <summary>
    ///     This method reports whether <paramref name="value" /> is a bare IP address without a
    ///     zone index (a zone names a local interface and never matches a client) or a
    ///     <c>/</c>-qualified network that <see cref="IPNetwork" /> parses, which also rejects a
    ///     prefix length out of range.
    /// </summary>
    /// <param name="value">The entry as typed; surrounding white space is ignored.</param>
    /// <param name="family">The address family of the entry when it parses.</param>
    public static bool TryParse(string value, out AddressFamily family)
    {
        family = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        string trimmed = value.Trim();

        if (trimmed.Contains('/'))
        {
            if (!IPNetwork.TryParse(trimmed, out IPNetwork network))
                return false;

            family = network.BaseAddress.AddressFamily;
            return true;
        }

        // A zone ("fe80::1%2") is rejected so the stored text equals what the matcher parses.
        if (trimmed.Contains('%') || !IPAddress.TryParse(trimmed, out IPAddress address))
            return false;

        family = address.AddressFamily;
        return true;
    }
}
