// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the query pipeline settings
///     (<c>DnsQueryProcessingPipelineOptions</c>) that <c>GET /api/dns-pipeline-settings</c>
///     returns: worker pool and queue sizing, and the UDP amplification guard. A <c>PATCH</c> to
///     the same endpoint accepts the same fields.
/// </summary>
/// <param name="WorkerCount">How many workers process queries in parallel. Defaults to the processor count.</param>
/// <param name="ChannelCapacity">How many received queries may wait in the queue for a worker. Defaults to 4096.</param>
/// <param name="MaxUdpPayloadSizeOctets">The largest UDP response sent, in octets. Defaults to 1232.</param>
/// <param name="MaxUdpAmplificationFactor">The largest allowed ratio of UDP response size to query size. Defaults to 20.</param>
/// <param name="MinUdpAmplificationFloorSizeOctets">The lowest the amplification cap can drop to, in octets. Defaults to 512, the RFC 1035 UDP limit.</param>
public record DnsPipelineSettingsDetail(
    int WorkerCount,
    int ChannelCapacity,
    int MaxUdpPayloadSizeOctets,
    double MaxUdpAmplificationFactor,
    int MinUdpAmplificationFloorSizeOctets
);
