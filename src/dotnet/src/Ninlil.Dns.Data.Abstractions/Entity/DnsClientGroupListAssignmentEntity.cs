// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class joins a <see cref="DnsClientGroupEntity" /> to one <see cref="DnsFilterListEntity" />
///     it should be filtered against -- a many-to-many relationship, since a list may be shared
///     across several client groups and a group may have several lists assigned.
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
