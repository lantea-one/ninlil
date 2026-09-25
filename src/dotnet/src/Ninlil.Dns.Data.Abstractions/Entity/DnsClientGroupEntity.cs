// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a named group of clients, matched by the rules in
///     <see cref="DnsClientGroupNetworkEntity" />, that share the filter lists in
///     <see cref="DnsClientGroupListAssignmentEntity" />. One group should have
///     <see cref="IsDefault" /> set: the group a client falls back to when it matches no other
///     group.
/// </summary>
public class DnsClientGroupEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the operator-chosen name of this group.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property indicates whether this is the fallback group for clients that match no
    ///     other group.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    ///     This property indicates whether this group is disabled. A disabled group's list
    ///     assignments are ignored, and a client that would match it falls back to the default
    ///     group.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains the time, in UTC, until which DNS resolution is blocked for every
    ///     member of this group: the group counterpart of <see cref="DnsClientEntity.PausedUntilUtc" />.
    ///     It is separate from <see cref="IsDisabled" />, which only affects filter lists; a pause
    ///     overrides everything. <see langword="null" /> or a past time means no pause.
    ///     <see cref="DnsClientEntity.PausedUntilUtc" /> explains why no sweep clears elapsed values.
    /// </summary>
    public DateTimeOffset? PausedUntilUtc { get; set; }
}
