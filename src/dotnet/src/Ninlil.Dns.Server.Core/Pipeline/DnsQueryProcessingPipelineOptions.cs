// Define our namespace.
namespace Ninlil.Dns.Server.Core.Pipeline;

/// <summary>
///     This class binds to the <c>DnsServer:Pipeline</c> section of the application's
///     configuration and controls <see cref="DnsQueryProcessingPipeline" />: its worker pool,
///     queue size and UDP response limits. The DNS pipeline settings endpoint edits it. The UDP
///     limits apply at once; the worker count and queue size apply when the pipeline restarts.
/// </summary>
public class DnsQueryProcessingPipelineOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Pipeline";

    /// <summary>
    ///     This property contains how many worker tasks process queries from the queue at once.
    ///     The default is one per processor. A worker waits while its query is resolved
    ///     (including upstream queries), so this also caps how many queries are in progress.
    /// </summary>
    public int WorkerCount { get; set; } = Environment.ProcessorCount;

    /// <summary>
    ///     This property contains how many unprocessed queries may wait in the queue. When it is
    ///     full, transports wait to enqueue.
    /// </summary>
    public int ChannelCapacity { get; set; } = 4096;

    /// <summary>
    ///     This property contains the largest response, in octets, sent over UDP, whatever EDNS
    ///     payload size the client advertises. It prevents IP fragmentation from clients that
    ///     claim a very large buffer. The default, 1232, matches
    ///     <see cref="Resolution.DnsQueryDispatcher.DefaultUdpPayloadSizeOctets" />. It is also the
    ///     payload size advertised in OPT records the pipeline adds.
    /// </summary>
    public int MaxUdpPayloadSizeOctets { get; set; } = 1232;

    /// <summary>
    ///     This property contains the largest multiple of a UDP query's size that its response may
    ///     be. It limits reflection attacks, where a small spoofed query makes this server send a
    ///     large response to a forged address. The cap is never below
    ///     <see cref="MinUdpAmplificationFloorSizeOctets" />. With the default of 20, a typical
    ///     40 to 70 octet EDNS query still allows an 800 to 1400 octet answer.
    /// </summary>
    public double MaxUdpAmplificationFactor { get; set; } = 20;

    /// <summary>
    ///     This property contains the lowest value the <see cref="MaxUdpAmplificationFactor" />
    ///     cap can drop to, so a very small query is not limited to an unusably small response.
    ///     The default, 512 octets, is the RFC 1035 UDP limit every client supports.
    /// </summary>
    public int MinUdpAmplificationFloorSizeOctets { get; set; } = 512;
}
