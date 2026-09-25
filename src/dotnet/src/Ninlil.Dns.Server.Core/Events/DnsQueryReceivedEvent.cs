using System.Net;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published when <see cref="Resolution.DnsQueryDispatcher" /> starts on a
///     query, before any resolver sees it. Queries the pipeline denies or answers before
///     dispatch (rate limit, cookie, schedule, filter, rewrite) do not publish it.
///     <para>
///         It carries a serializable summary rather than the <see cref="Resolution.DnsQueryContext" />,
///         so it can cross a message broker.
///     </para>
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the query was received.</param>
/// <param name="Protocol">The transport the query arrived over.</param>
/// <param name="RemoteAddress">The client's source address.</param>
/// <param name="QueriedName">The queried name in presentation form, or <see langword="null" /> if the query had no question.</param>
/// <param name="QueriedType">The queried record type, or <see langword="null" /> if the query had no question.</param>
public record DnsQueryReceivedEvent(
    DateTimeOffset OccurredAtUtc,
    DnsTransportProtocolEnum Protocol,
    IPAddress RemoteAddress,
    string QueriedName,
    DnsResourceRecordTypeEnum? QueriedType
) : IDnsServerEvent;
