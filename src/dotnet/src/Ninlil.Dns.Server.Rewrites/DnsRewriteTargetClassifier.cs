using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites;

/// <summary>
///     This class decides which <see cref="DnsRewriteTargetKindEnum" /> a
///     <c>DnsRewriteEntity.Target</c> string answers as: an IP address is A or AAAA, and a domain
///     name is a CNAME. It is the one place this rule lives. <c>DnsRewritesController</c> and
///     <c>SafeSearchController</c> call it to validate targets and to report their kind, and
///     <see cref="DnsRewriteEngine" /> calls it to build answers.
/// </summary>
public static class DnsRewriteTargetClassifier
{
    /// <summary>
    ///     This method attempts to classify a target string.
    /// </summary>
    /// <param name="target">The target string to classify.</param>
    /// <param name="kind">The classified kind, if classification succeeded.</param>
    /// <param name="error">A human-readable description of why classification failed, if it did.</param>
    /// <returns>A <see langword="bool" /> indicating whether classification succeeded.</returns>
    public static bool TryClassify(
        string target,
        out DnsRewriteTargetKindEnum kind,
        out string error
    )
    {
        kind = default;
        error = null;

        if (string.IsNullOrWhiteSpace(target))
        {
            error = "'target' is required.";
            return false;
        }

        // An IP literal is checked first; its address family decides A or AAAA.
        if (IPAddress.TryParse(target, out IPAddress address))
        {
            kind =
                address.AddressFamily == AddressFamily.InterNetworkV6
                    ? DnsRewriteTargetKindEnum.Aaaa
                    : DnsRewriteTargetKindEnum.A;
            return true;
        }

        // Anything else must be a valid domain name to be a CNAME target. Rejecting the rest
        // here means only rows written without this validation can reach DnsRewriteEngine
        // unclassifiable.
        if (!DnsName.TryParse(target, out _, out string nameError))
        {
            error =
                $"'{target}' is neither a valid IP address nor a valid domain name: {nameError}";
            return false;
        }

        kind = DnsRewriteTargetKindEnum.Cname;
        return true;
    }
}
