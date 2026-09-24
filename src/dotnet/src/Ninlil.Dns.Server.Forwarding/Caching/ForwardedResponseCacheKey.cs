using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Caching;

/// <summary>
///     This record struct is what two forwarded questions must share for one's cached answer to
///     be a correct answer to the other. The question alone is not enough: the same name can
///     legitimately get a different answer depending on what else the query asked for and where
///     it was sent, so every such input is part of the key.
/// </summary>
/// <param name="Name">
///     The queried name. <see cref="DnsName" /> compares case-insensitively, so
///     <c>WWW.Example.com</c> and <c>www.example.com</c> (or a client using 0x20 case
///     randomization) share an entry.
/// </param>
/// <param name="Type">The queried type.</param>
/// <param name="Class">The queried class.</param>
/// <param name="DnssecOk">
///     The client's DO bit: with it the upstream includes RRSIGs and NSEC proofs, without it
///     those are left out, so the two answers are different messages.
/// </param>
/// <param name="CheckingDisabled">
///     The client's CD bit: a validating upstream answers SERVFAIL for a bogus name without it
///     and hands over the data with it.
/// </param>
/// <param name="RecursionDesired">The client's RD bit, which the query is forwarded with unchanged.</param>
/// <param name="HasEdns">
///     Whether the client spoke EDNS at all. An answer to an EDNS query carries an OPT record a
///     client that never sent one must not receive (RFC 6891 §7).
/// </param>
/// <param name="Validated">
///     Whether this server was re-validating forwarded answers against DNSSEC when the answer
///     was stored, so turning validation on never serves an answer that was never checked.
/// </param>
/// <param name="UpstreamSet">
///     The forwarder set the query went to (every definition string, in order). Conditional
///     forwarding sends a name under a forwarder zone to that zone's own servers, which can
///     answer differently from the global forwarders (a split-horizon internal zone, for
///     example), so answers from different sets must never be mixed. It also means editing a
///     forwarder list starts from an empty cache for that set, without an explicit flush.
/// </param>
/// <param name="ClientSubnet">
///     The EDNS Client Subnet option sent upstream, as hex, or <see langword="null" /> when none
///     was. An upstream that honours ECS tailors its answer to that subnet, so it is only
///     reused for a query that would disclose the same subnet (RFC 7871 §7.3.1 allows a
///     narrower reuse by returned scope; keying on the source subnet is the simple, never-wrong
///     choice).
/// </param>
public readonly record struct ForwardedResponseCacheKey(
    DnsName Name,
    DnsResourceRecordTypeEnum Type,
    DnsClassEnum Class,
    bool DnssecOk,
    bool CheckingDisabled,
    bool RecursionDesired,
    bool HasEdns,
    bool Validated,
    string UpstreamSet,
    string ClientSubnet
)
{
    /// <summary>
    ///     This method builds the key for one forwarded query.
    /// </summary>
    /// <param name="clientRequest">The client's own request, for the question and header bits.</param>
    /// <param name="outboundRequest">
    ///     The request actually sent upstream, which differs from <paramref name="clientRequest" />
    ///     only by an attached ECS option.
    /// </param>
    /// <param name="upstreamSet">The forwarder definition strings the query goes to, in order.</param>
    /// <param name="validated">Whether forwarded answers are being DNSSEC re-validated.</param>
    /// <returns>The key.</returns>
    public static ForwardedResponseCacheKey Create(
        DnsMessage clientRequest,
        DnsMessage outboundRequest,
        IReadOnlyList<string> upstreamSet,
        bool validated
    )
    {
        DnsQuestion question = clientRequest.Questions[0];
        DnsEdnsOption clientSubnet = outboundRequest.Edns?.Options.FirstOrDefault(option =>
            option.Code == (ushort)EdnsOptionCodeEnum.ClientSubnet
        );

        return new ForwardedResponseCacheKey(
            question.Name,
            question.Type,
            question.Class,
            clientRequest.Edns?.DnssecOk == true,
            clientRequest.CheckingDisabled,
            clientRequest.RecursionDesired,
            clientRequest.Edns is not null,
            validated,
            // A newline cannot appear inside a definition string, so joining on it keeps two
            // different lists from ever producing the same text.
            string.Join('\n', upstreamSet),
            clientSubnet is null ? null : Convert.ToHexString(clientSubnet.Data)
        );
    }
}
