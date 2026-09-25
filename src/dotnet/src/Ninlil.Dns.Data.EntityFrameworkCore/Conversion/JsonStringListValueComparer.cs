using Microsoft.EntityFrameworkCore.ChangeTracking;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Conversion;

/// <summary>
///     This class tells the Entity Framework Core change tracker how to compare two
///     <see cref="List{T}" /> of <see cref="string" /> values and how to snapshot one. It pairs
///     with <see cref="JsonStringListValueConverter" />. Without it, a list property such as
///     <c>ZoneEntity.PrimaryServerAddresses</c> would be compared by reference, and changes made
///     to the list in place would not be saved.
/// </summary>
public class JsonStringListValueComparer()
    : ValueComparer<List<string>>(
        (left, right) => (left ?? new List<string>()).SequenceEqual(right ?? new List<string>()),
        list => list == null ? 0 : list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
        list => list == null ? new List<string>() : new List<string>(list)
    );
