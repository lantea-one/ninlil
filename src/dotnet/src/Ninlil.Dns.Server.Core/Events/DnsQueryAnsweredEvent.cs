using System.Net;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published by <see cref="Pipeline.DnsQueryProcessingPipeline" /> once per
///     answered query, just before the response is sent, whichever step produced it (a
///     resolver, the dispatcher, a cookie, schedule or filter denial, or a rewrite). Queries
///     dropped without a reply publish nothing. The query log, client presence tracking and the
///     live events hub subscribe to it.
///     <para>
///         It carries a serializable summary rather than the <see cref="Resolution.DnsQueryContext" />
///         and <see cref="Common.Wire.DnsMessage" />, so it can cross a message broker.
///     </para>
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the query finished being processed.</param>
/// <param name="Protocol">The transport the query arrived over.</param>
/// <param name="RemoteAddress">The client's source address.</param>
/// <param name="QueriedName">The queried name in presentation form, or <see langword="null" /> if the query had no question.</param>
/// <param name="QueriedType">The queried record type, or <see langword="null" /> if the query had no question.</param>
/// <param name="ResponseCode">The response code the answer was sent with.</param>
/// <param name="AnswerCount">How many records the Answer section carried, counted before any UDP truncation.</param>
public record DnsQueryAnsweredEvent(
    DateTimeOffset OccurredAtUtc,
    DnsTransportProtocolEnum Protocol,
    IPAddress RemoteAddress,
    string QueriedName,
    DnsResourceRecordTypeEnum? QueriedType,
    DnsResponseCodeEnum ResponseCode,
    int AnswerCount
) : IDnsServerEvent;
