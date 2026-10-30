// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a DNS rewrite: a lightweight <c>domain -&gt; target</c> override that
///     <c>DnsRewriteEngine</c> applies early in <c>DnsQueryProcessingPipeline</c>, before zone,
///     forwarder and recursive resolution. It is its own
///     entity rather than records in a hidden "local overrides" <see cref="ZoneEntity" />, which
///     would DNSSEC-sign every override and need apex and SOA bookkeeping for nothing.
/// </summary>
public class DnsRewriteEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the queried domain this rewrite matches. Without a <c>*</c> it
    ///     matches only that exact name, not its subdomains. This differs from
    ///     <c>DnsFilterListEntryEntity</c> on purpose: a rewrite overrides one host, and an operator
    ///     who wants the subtree writes <c>*.example.com</c>. With a <c>*</c> it is a shell-style
    ///     glob, which <c>DnsRewriteEngine</c> turns into an anchored regular expression with a
    ///     timeout, the same technique as <c>DnsFilterEngine.BuildWildcardRegexPattern</c> (copied to
    ///     avoid a project dependency).
    /// </summary>
    public string Domain { get; set; }

    /// <summary>
    ///     This property contains what <see cref="Domain" /> resolves to: an IP address (an A or AAAA
    ///     rewrite, recognized by <c>IPAddress.TryParse</c>) or a domain name (a CNAME rewrite, a
    ///     valid <see cref="Ninlil.Dns.Common.Wire.DnsName" />). There is no record-type field; the
    ///     type is computed from this value (<see cref="Ninlil.Dns.Common.Enum.DnsRewriteTargetKindEnum" />), so it cannot
    ///     drift out of step with it.
    /// </summary>
    public string Target { get; set; }

    /// <summary>
    ///     This property indicates whether this rewrite is disabled. A disabled rewrite is kept but
    ///     not used at query time.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this rewrite.
    /// </summary>
    public string Comments { get; set; }

    /// <summary>
    ///     This property, when set, limits this rewrite to queries whose resolved client identity
    ///     (see <c>Ninlil.Dns.Server.Core.ClientIdentity.IClientIdentityResolver</c>) is this
    ///     <see cref="DnsClientEntity" />. Unscoped rewrites (this and <see cref="ClientGroupId" />
    ///     both <see langword="null" />) still apply to everyone; scoping only adds rewrites for a
    ///     narrower audience and never overrides a global one. <see langword="null" /> means the
    ///     rewrite is global. It is exclusive with <see cref="ClientGroupId" />. The write paths
    ///     never set both, but storage does not enforce it; <c>DnsRewriteEngine.AppliesToClient</c>
    ///     treats a row with both as scoped to the client.
    /// </summary>
    public Guid? ClientId { get; set; }

    /// <summary>
    ///     This property, when set, limits this rewrite to queries whose resolved client belongs to
    ///     this <see cref="DnsClientGroupEntity" /> (through <see cref="DnsClientEntity.ClientGroupId" />).
    ///     It follows the same rules as <see cref="ClientId" /> and is exclusive with it.
    /// </summary>
    public Guid? ClientGroupId { get; set; }
}
