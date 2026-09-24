using System.Net;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published once <see cref="Resolution.IDnsQueryDispatcher" /> has a final
///     answer for a query, whether that answer came from a resolver or the dispatcher itself (a
///     REFUSED, for example, when nothing claimed the query). This is the event a metrics
///     subscriber should listen for to count queries, response codes, and latency.
///     <para>
///         Carries a serializable summary of the query and response rather than the live
///         <see cref="Resolution.DnsQueryContext" />/<see cref="Common.Wire.DnsMessage" />
///         themselves -- required for this event to ever cross a message broker.
///     </para>
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the query finished being processed.</param>
/// <param name="Protocol">The transport the query arrived over.</param>
/// <param name="RemoteAddress">The querying client's own source address.</param>
/// <param name="QueriedName">The queried name's own presentation form, or <see langword="null" /> if the query carried no question at all.</param>
/// <param name="QueriedType">The queried record type, or <see langword="null" /> if the query carried no question at all.</param>
/// <param name="ResponseCode">The response code the answer was sent with.</param>
/// <param name="AnswerCount">How many records the answer's own Answer section carried.</param>
public record DnsQueryAnsweredEvent(
    DateTimeOffset OccurredAtUtc,
    DnsTransportProtocolEnum Protocol,
    IPAddress RemoteAddress,
    string QueriedName,
    DnsResourceRecordTypeEnum? QueriedType,
    DnsResponseCodeEnum ResponseCode,
    int AnswerCount
) : IDnsServerEvent;
