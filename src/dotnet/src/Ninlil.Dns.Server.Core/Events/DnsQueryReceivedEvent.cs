using System.Net;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published the moment <see cref="Resolution.IDnsQueryDispatcher" /> begins
///     processing a query, before any resolver has had a chance to look at it.
///     <para>
///         Carries a serializable summary of the query rather than the live
///         <see cref="Resolution.DnsQueryContext" /> itself -- required for this event to ever
///         cross a message broker.
///     </para>
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the query was received.</param>
/// <param name="Protocol">The transport the query arrived over.</param>
/// <param name="RemoteAddress">The querying client's own source address.</param>
/// <param name="QueriedName">The queried name's own presentation form, or <see langword="null" /> if the query carried no question at all.</param>
/// <param name="QueriedType">The queried record type, or <see langword="null" /> if the query carried no question at all.</param>
public record DnsQueryReceivedEvent(
    DateTimeOffset OccurredAtUtc,
    DnsTransportProtocolEnum Protocol,
    IPAddress RemoteAddress,
    string QueriedName,
    DnsResourceRecordTypeEnum? QueriedType
) : IDnsServerEvent;
