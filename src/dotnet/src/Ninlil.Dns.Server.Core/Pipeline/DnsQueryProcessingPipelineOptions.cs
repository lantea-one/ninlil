// Define our namespace.
namespace Ninlil.Dns.Server.Core.Pipeline;

/// <summary>
///     This class binds to the <c>DnsServer:Pipeline</c> section of the application's
///     configuration and controls how <see cref="DnsQueryProcessingPipeline" /> sizes its worker
///     pool and inbound queue.
/// </summary>
public class DnsQueryProcessingPipelineOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Pipeline";

    /// <summary>
    ///     This property contains how many worker tasks concurrently decode, resolve, and encode
    ///     queries pulled from the inbound queue. Defaults to one per available processor, since
    ///     query processing is a mix of CPU-bound work (wire parsing) and short-lived I/O (a
    ///     database lookup, an upstream query), which scales well with the number of cores.
    /// </summary>
    public int WorkerCount { get; set; } = Environment.ProcessorCount;

    /// <summary>
    ///     This property contains how many not-yet-processed queries may be queued at once.
    ///     Every transport's own receive loop enqueues into this same queue rather than
    ///     processing a query itself, so that a slow query never blocks that transport from
    ///     accepting the next one off the wire.
    /// </summary>
    public int ChannelCapacity { get; set; } = 4096;

    /// <summary>
    ///     This property contains the largest response, in bytes, this pipeline will ever send
    ///     over UDP, regardless of what a requestor's own EDNS(0) payload size claims -- a safety
    ///     ceiling against a client declaring an unrealistically large buffer and risking IP
    ///     fragmentation. 1232 octets matches <see cref="Resolution.DnsQueryDispatcher.DefaultUdpPayloadSizeOctets" />,
    ///     the value the DNS operator community converged on in 2020.
    /// </summary>
    public int MaxUdpPayloadSizeOctets { get; set; } = 1232;

    /// <summary>
    ///     This property contains the largest multiple of a UDP query's own raw wire size that
    ///     its response is ever allowed to be, an amplification-factor cap independent of (and
    ///     typically tighter than) <see cref="MaxUdpPayloadSizeOctets" />'s flat ceiling -- a
    ///     minimal, no-EDNS spoofed query is the classic reflection/amplification attack shape,
    ///     and capping the *ratio* bounds how much bigger than the query a response can be sent
    ///     to a claimed (possibly forged) source address, regardless of what payload size an
    ///     EDNS-aware requestor claims to support. 20 is a conservative default: a typical
    ///     EDNS-aware query (roughly 40-70 bytes once its own OPT pseudo-record is included)
    ///     still comfortably fits a signed answer under this ratio, while a bare, no-EDNS query
    ///     is bounded by <see cref="MinUdpAmplificationFloorSizeOctets" /> instead.
    /// </summary>
    public double MaxUdpAmplificationFactor { get; set; } = 20;

    /// <summary>
    ///     This property contains the smallest amplification-factor ceiling
    ///     <see cref="MaxUdpAmplificationFactor" /> is ever allowed to compute down to, so a tiny
    ///     (as little as 12-byte, header-only) query is not clamped to an unusably small
    ///     response. 512 octets matches RFC 1035's own original, pre-EDNS UDP message size, the
    ///     size every DNS implementation has always been required to support without any
    ///     negotiation at all.
    /// </summary>
    public int MinUdpAmplificationFloorSizeOctets { get; set; } = 512;
}
