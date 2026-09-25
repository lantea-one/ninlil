// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class joins a <see cref="DnsClientEntity" /> to a <see cref="DnsFilterListEntity" /> it
///     is filtered against. It mirrors <see cref="DnsClientGroupListAssignmentEntity" />, keyed by
///     <see cref="ClientId" /> instead of <c>ClientGroupId</c>. A client-level assignment adds to
///     the lists of the client's matched <see cref="DnsClientGroupEntity" /> (matched by network,
///     DNS-over-HTTPS ClientID or the <see cref="DnsClientEntity.ClientGroupId" /> override).
///     <c>DnsFilterEngine.ResolveApplicableListsAsync</c> combines the two.
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
