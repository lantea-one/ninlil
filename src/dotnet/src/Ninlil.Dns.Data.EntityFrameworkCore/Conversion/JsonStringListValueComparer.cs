using Microsoft.EntityFrameworkCore.ChangeTracking;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Conversion;

/// <summary>
///     This class tells Entity Framework Core's change tracker how to tell two
///     <see cref="List{T}" /> of <see cref="string" /> values apart, and how to take an
///     independent snapshot of one. Without this, a mutable collection property such as
///     <c>ZoneEntity.PrimaryServerAddresses</c> would be compared by reference, so editing its
///     contents in place would silently go undetected by <c>SaveChangesAsync</c>.
/// </summary>
public class JsonStringListValueComparer()
    : ValueComparer<List<string>>(
        (left, right) => (left ?? new List<string>()).SequenceEqual(right ?? new List<string>()),
        list => list == null ? 0 : list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
        list => list == null ? new List<string>() : new List<string>(list)
    );
