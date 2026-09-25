// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one time-series measurement, such as "queries answered from cache in
///     this interval", kept for the dashboard. Names and categories are free-form strings rather
///     than an enumeration, so new counters need no schema change.
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
    ///     This property contains extra dimensions of the measurement (for example the zone or DHCP
    ///     scope it applies to), as a JSON object.
    /// </summary>
    public string DimensionsJson { get; set; }
}
