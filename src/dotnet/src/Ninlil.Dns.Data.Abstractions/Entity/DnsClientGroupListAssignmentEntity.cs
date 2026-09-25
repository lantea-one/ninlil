// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class joins a <see cref="DnsClientGroupEntity" /> to a <see cref="DnsFilterListEntity" />
///     it is filtered against. The relationship is many-to-many: a list can be shared by several
///     groups and a group can have several lists.
/// </summary>
public class DnsClientGroupListAssignmentEntity : EntityBase
{
    /// <summary>
    ///     This property contains the assigned <see cref="DnsClientGroupEntity" />.
    /// </summary>
    public Guid ClientGroupId { get; set; }

    /// <summary>
    ///     This property contains the <see cref="DnsFilterListEntity" /> assigned to that group.
    /// </summary>
    public Guid ListId { get; set; }
}
