// Define our namespace.
namespace Ninlil.Dns.Server.Core.Pipeline;

/// <summary>
///     This delegate tells a transport that <see cref="DnsQueryProcessingPipeline" /> ended a
///     query without a reply. A transport supplies one in <see cref="DnsInboundMessage.OnDropped" />
///     when it must release something per query (an HTTP request, a stream slot). For every
///     query the pipeline takes off its queue it calls either
///     <see cref="DnsResponseSenderAsync" /> or this delegate, exactly once. It must return
///     quickly and not throw.
/// </summary>
/// <param name="reason">Why the query was dropped.</param>
public delegate void DnsQueryDropNotifier(DnsQueryDropReasonEnum reason);
