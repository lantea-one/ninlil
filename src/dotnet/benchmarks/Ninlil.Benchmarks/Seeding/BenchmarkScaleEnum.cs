// Define our namespace.
namespace Ninlil.Benchmarks.Seeding;

/// <summary>
///     This enum selects the size of the seeded data set (the <c>--scale</c> option).
/// </summary>
public enum BenchmarkScaleEnum
{
    /// <summary>
    ///     About 1/100 of <see cref="Large" />. A smoke size: it proves every benchmark runs on an
    ///     engine, and its timings say little.
    /// </summary>
    Small = 0,

    /// <summary>
    ///     The reference size: 1,000,000 query-log entries, 1,000 zones plus one of 50,000
    ///     records, three filter lists of 100,000 entries and so on.
    /// </summary>
    Large = 1,
}
