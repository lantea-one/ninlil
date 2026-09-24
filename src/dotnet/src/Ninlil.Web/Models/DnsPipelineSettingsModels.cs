// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>DnsQueryProcessingPipelineOptions</c> -- worker-pool/queue sizing plus UDP
///     amplification-guard tuning. This record's own fields are the exact same ones a
///     <c>PATCH</c> to the same endpoint accepts.
/// </summary>
public record DnsPipelineSettingsDetail(
    int WorkerCount,
    int ChannelCapacity,
    int MaxUdpPayloadSizeOctets,
    double MaxUdpAmplificationFactor,
    int MinUdpAmplificationFloorSizeOctets
);
