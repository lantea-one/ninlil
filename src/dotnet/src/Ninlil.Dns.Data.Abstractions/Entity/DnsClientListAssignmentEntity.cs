// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class joins a single <see cref="DnsClientEntity" /> to one <see cref="DnsFilterListEntity" />
///     it should be filtered against -- a many-to-many relationship, since a list may be shared
///     across several clients and a client may have several lists assigned directly to it. This
///     mirrors <see cref="DnsClientGroupListAssignmentEntity" /> exactly, but keyed by
///     <see cref="ClientId" /> instead of <c>ClientGroupId</c>: a client-level assignment is an
///     <em>additional</em> way for a filter list to apply to a client, unioned with whatever
///     list(s) its own matched <see cref="DnsClientGroupEntity" /> (by network, DNS-over-HTTPS
///     ClientID, or its own <see cref="DnsClientEntity.ClientGroupId" /> override) already
///     contributes -- see <c>DnsFilterEngine.ResolveApplicableListsAsync</c> for the real
///     resolution logic that consumes this table.
/// </summary>
public class DnsClientListAssignmentEntity : EntityBase
{
    /// <summary>
    ///     This property contains the assigned <see cref="DnsClientEntity" />.
    /// </summary>
    public Guid ClientId { get; set; }

    /// <summary>
    ///     This property contains the <see cref="DnsFilterListEntity" /> assigned to that client.
    /// </summary>
    public Guid ListId { get; set; }
}
