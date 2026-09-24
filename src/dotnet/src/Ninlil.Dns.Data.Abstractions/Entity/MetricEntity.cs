// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single time-series measurement -- for example, "how many queries
///     were answered from cache in this interval" -- captured for reporting on the dashboard.
///     Metric names and categories are deliberately left as free-form strings rather than a fixed
///     enumeration, since new counters are expected to be added over time as the protocol and
///     server implementations grow, without requiring a schema change each time.
/// </summary>
public class MetricEntity : EntityBase
{
    /// <summary>
    ///     This property contains the UTC date and time the measurement applies to.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; set; }

    /// <summary>
    ///     This property contains the broad grouping the metric belongs to, such as
    ///     <c>"dns"</c>, <c>"dhcp-v4"</c>, or <c>"dhcp-v6"</c>.
    /// </summary>
    public string Category { get; set; }

    /// <summary>
    ///     This property contains the specific metric's name, such as
    ///     <c>"queries.answered.cache"</c>.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the measured value for this interval.
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    ///     This property contains any additional dimensions the measurement should be broken down
    ///     by (for example, which zone or which DHCP scope it applies to), serialized as a JSON
    ///     object.
    /// </summary>
    public string DimensionsJson { get; set; }
}
