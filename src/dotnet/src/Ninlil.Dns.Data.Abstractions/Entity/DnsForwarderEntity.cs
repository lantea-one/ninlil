using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one server-wide upstream forwarder. The forwarding resolver
///     (<c>Server.Forwarding.ForwarderQueryResolver</c>) uses this set before falling back to
///     iterative resolution in <c>Server.Recursive</c>. A zone of type
///     <see cref="ZoneTypeEnum.Forwarder" /> uses its <see cref="ZoneEntity.ForwarderAddresses" />
///     instead and ignores this set.
/// </summary>
public class DnsForwarderEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the operator-chosen name of this forwarder.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the forwarder's definition string: a plain address, a
    ///     <c>tls://</c>, <c>https://</c> or <c>quic://</c> URL, or an <c>sdns://</c> DNSCrypt
    ///     stamp. It is stored as written and parsed at runtime, because
    ///     <see cref="ZoneEntity.ForwarderAddresses" /> uses the same strings.
    /// </summary>
    public string DefinitionString { get; set; }

    /// <summary>
    ///     This property contains this forwarder's position among the global forwarders; lower
    ///     values are preferred.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    ///     This property indicates whether this forwarder is disabled. A disabled forwarder is kept
    ///     but never health-checked or sent a query.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this forwarder.
    /// </summary>
    public string Comments { get; set; }
}
