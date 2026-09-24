// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single DNS rewrite: a lightweight
///     <c>domain -&gt; target</c> override consulted early in
///     <c>DnsQueryProcessingPipeline</c>, before zone/forwarder/recursive resolution -- the
///     AdGuard Home "DNS rewrites" feature. Deliberately a dedicated entity, not sugar over an
///     implicit always-present "local overrides" zone (<see cref="ZoneEntity" />): a pseudo-zone
///     approach would DNSSEC-sign every simple override for no reason and add zone-apex/SOA
///     bookkeeping a lightweight override has no business needing.
/// </summary>
public class DnsRewriteEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the queried domain this rewrite matches. With no <c>*</c>
    ///     character, it matches only that exact name (deliberately <b>not</b> also every
    ///     subdomain -- a real, documented difference from <c>DnsFilterListEntryEntity</c>'s own
    ///     "domain and every subdomain" convention: a rewrite is meant to override one specific
    ///     host, not silently redirect its whole subtree; an operator who genuinely wants that
    ///     writes an explicit <c>*.example.com</c> wildcard entry instead). With a <c>*</c>
    ///     character present, it is matched as a shell-style glob (translated to an anchored,
    ///     timeout-guarded regex internally by <c>DnsRewriteEngine</c>, the same technique --
    ///     independently implemented, to avoid a cross-project dependency -- that
    ///     <c>DnsFilterEngine.BuildWildcardRegexPattern</c> already established).
    /// </summary>
    public string Domain { get; set; }

    /// <summary>
    ///     This property contains what <see cref="Domain" /> resolves to -- either a literal IP
    ///     address (an A/AAAA-style rewrite, classified by <c>IPAddress.TryParse</c>) or another
    ///     domain name (a CNAME-style rewrite, classified as a syntactically valid
    ///     <see cref="Ninlil.Dns.Common.Wire.DnsName" /> once IP parsing fails). There is no separate, explicit
    ///     "record type" field -- the answer's own record type is always computed fresh from this
    ///     value, the same "classify the real, already-validated data, don't store a redundant
    ///     parallel field that could drift out of sync with it" convention
    ///     <c>ForwarderDetail.Protocol</c> already establishes for a forwarder's own
    ///     <c>definitionString</c>.
    /// </summary>
    public string Target { get; set; }

    /// <summary>
    ///     This property indicates whether this rewrite is currently disabled. A disabled
    ///     rewrite is kept on record but never consulted at query time.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this rewrite.
    /// </summary>
    public string Comments { get; set; }

    /// <summary>
    ///     This property, when set, narrows this rewrite so it only ever applies to a query whose
    ///     own resolved client identity (see <c>Ninlil.Dns.Server.Core.ClientIdentity.IClientIdentityResolver</c>)
    ///     matches this exact <see cref="DnsClientEntity" /> -- <em>in addition to</em> every
    ///     unscoped (both this property and <see cref="ClientGroupId" /> <see langword="null" />)
    ///     rewrite still applying to every query as before; scoping only ever narrows an
    ///     <em>additional</em> rewrite to a specific audience, it never removes or overrides an
    ///     existing global one. <see langword="null" /> (the case for every row created before this
    ///     property existed, and for every row the ordinary <c>DnsRewritesController</c> create/
    ///     replace path writes today) means this rewrite is unconditionally global, exactly
    ///     today's behavior. Mutually exclusive with <see cref="ClientGroupId" /> -- a row the real
    ///     write paths ever produce never has both set at once, though nothing at the storage layer
    ///     itself enforces that (see <c>DnsRewriteEngine.AppliesToClient</c> for how a row with
    ///     both set is safely treated, defensively, as scoped to the client alone).
    /// </summary>
    public Guid? ClientId { get; set; }

    /// <summary>
    ///     This property, when set, narrows this rewrite so it only ever applies to a query whose
    ///     own resolved client identity belongs to this exact <see cref="DnsClientGroupEntity" />
    ///     (via that client's own <see cref="DnsClientEntity.ClientGroupId" />) -- the same
    ///     additive-only, never-overriding-global convention <see cref="ClientId" />'s own doc
    ///     comment describes, and mutually exclusive with it the same way.
    /// </summary>
    public Guid? ClientGroupId { get; set; }
}
