// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a named group of clients (matched by source IP/network, via
///     <see cref="DnsClientGroupNetworkEntity" />) that share the same set of assigned
///     block/allow lists (<see cref="DnsClientGroupListAssignmentEntity" />). Exactly one group
///     should have <see cref="IsDefault" /> set -- the group a client that matches no other
///     group's own networks falls back to.
/// </summary>
public class DnsClientGroupEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains an operator-chosen, human-readable name for this group.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property indicates whether this is the fallback group a client matching no other
    ///     group's own networks is treated as belonging to.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    ///     This property indicates whether this group is currently disabled. A disabled group's
    ///     own list assignments are never consulted; a client that would otherwise match it falls
    ///     back to the default group instead.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains the instant, in UTC, until which every member of this group has
    ///     its own DNS resolution entirely blocked -- the group-scoped counterpart of
    ///     <see cref="DnsClientEntity.PausedUntilUtc" />, a wholly separate mechanism from
    ///     <see cref="IsDisabled" /> above (that flag only ever affects filter-list resolution;
    ///     this one is an unconditional kill switch overriding everything). <see langword="null" />,
    ///     or any instant that has already passed, means this group is not currently pausing its
    ///     members; see <see cref="DnsClientEntity.PausedUntilUtc" />'s own doc comment for why no
    ///     background sweep is needed to clear an elapsed value.
    /// </summary>
    public DateTimeOffset? PausedUntilUtc { get; set; }
}
