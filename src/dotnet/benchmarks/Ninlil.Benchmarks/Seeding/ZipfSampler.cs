// Define our namespace.
namespace Ninlil.Benchmarks.Seeding;

/// <summary>
///     This class draws ranks from a Zipf distribution with exponent 1 (rank <c>k</c> has weight
///     <c>1 / (k + 1)</c>), the shape of real query traffic: a few clients and names dominate.
///     The data-set generator uses it with a seeded <see cref="Random" />, so the data is the
///     same on every run.
/// </summary>
public sealed class ZipfSampler
{
    private readonly double[] _cumulative;

    /// <summary>
    ///     This constructor builds the cumulative weights for <paramref name="count" /> ranks.
    /// </summary>
    /// <param name="count">The number of distinct ranks, at least 1.</param>
    public ZipfSampler(int count)
    {
        _cumulative = new double[count];
        double sum = 0;

        for (int rank = 0; rank < count; rank++)
        {
            sum += 1.0 / (rank + 1);
            _cumulative[rank] = sum;
        }
    }

    /// <summary>
    ///     This method returns a rank from 0 (the most frequent) to count - 1.
    /// </summary>
    public int Next(Random random)
    {
        double target = random.NextDouble() * _cumulative[^1];
        int index = Array.BinarySearch(_cumulative, target);
        return index >= 0 ? index : ~index;
    }
}
