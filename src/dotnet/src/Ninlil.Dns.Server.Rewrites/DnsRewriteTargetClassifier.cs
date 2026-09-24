using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites;

/// <summary>
///     This class classifies a <c>DnsRewriteEntity.Target</c> string into the
///     <see cref="DnsRewriteTargetKindEnum" /> it answers as, the single, real place this
///     classification logic exists -- <c>DnsRewritesController</c> (validation at write time,
///     and computing the read-only classification for a REST response) and
///     <see cref="DnsRewriteEngine" /> (answer construction at query time) both call this
///     directly, mirroring <c>FilterListEntriesController</c>'s own precedent of calling
///     <c>DnsFilterEngine.CompileOrNull</c>/<c>DnsFilterEngine.BuildWildcardRegexPattern</c>
///     directly rather than duplicating the logic or inventing a second interface just for
///     validation.
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

        // What/why: an IP-literal target is checked first, since a well-formed IPv4/IPv6 address
        // is never also a well-formed domain name -- how: the family the literal parsed as
        // decides A vs. AAAA directly, no separate domain-name parse is attempted for this
        // target at all once this branch succeeds.
        if (IPAddress.TryParse(target, out IPAddress address))
        {
            kind =
                address.AddressFamily == AddressFamily.InterNetworkV6
                    ? DnsRewriteTargetKindEnum.Aaaa
                    : DnsRewriteTargetKindEnum.A;
            return true;
        }

        // What/why/how: anything that isn't an IP literal is classified as a CNAME target only if
        // it is itself a structurally valid domain name -- a target that is neither is rejected
        // outright here, so DnsRewriteEngine never has to handle an unclassifiable value at query
        // time (its own defensive fallback exists only for a row some non-REST write path
        // inserted without this validation).
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
