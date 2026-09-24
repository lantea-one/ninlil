using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single globally configured upstream forwarder -- the server-wide
///     default forwarder set <c>Server.Core</c>'s dispatcher-level forwarding resolver consults
///     before falling back to <c>Server.Recursive</c>'s own iterative resolution. A zone whose
///     <see cref="ZoneEntity.Type" /> is <see cref="ZoneTypeEnum.Forwarder" /> instead uses
///     its own <see cref="ZoneEntity.ForwarderAddresses" /> in place of this global set, entirely
///     independently of this entity.
/// </summary>
public class DnsForwarderEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains an operator-chosen, human-readable name for this forwarder.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the forwarder's own Technitium/AdGuard Home-style definition
    ///     string (a plain address, a <c>tls://</c>/<c>https://</c>/<c>quic://</c> URL, or an
    ///     <c>sdns://</c> DNSCrypt stamp) -- parsed at runtime, never persisted pre-parsed, since
    ///     the same string is also how this project's own zone-level
    ///     <see cref="ZoneEntity.ForwarderAddresses" /> are written.
    /// </summary>
    public string DefinitionString { get; set; }

    /// <summary>
    ///     This property contains this forwarder's own position among every other configured
    ///     global forwarder -- lower values are preferred first.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    ///     This property indicates whether this forwarder is currently disabled. A disabled
    ///     forwarder is kept on record but never consulted, health-checked, or sent a query.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this forwarder.
    /// </summary>
    public string Comments { get; set; }
}
