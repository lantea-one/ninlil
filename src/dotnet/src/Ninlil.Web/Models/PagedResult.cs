// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class wraps one page of a larger list, the shape the paginated list endpoints return.
///     Paging is by offset and limit: with a capped page size that is enough at homelab and
///     small-office scale, so cursor-based paging is not needed.
/// </summary>
/// <param name="Items">The page's items.</param>
/// <param name="Skip">How many items were skipped before this page began.</param>
/// <param name="Take">The maximum number of items this page could contain.</param>
/// <param name="TotalCount">The total number of items across every page.</param>
/// <typeparam name="TItem">The type of item this page carries.</typeparam>
public record PagedResult<TItem>(IReadOnlyList<TItem> Items, int Skip, int Take, int TotalCount);

/// <summary>
///     This class carries the offset and limit query-string parameters the paginated list
///     endpoints accept.
/// </summary>
public class PaginationQuery
{
    /// <summary>
    ///     This constant contains the page size used when a caller does not give one.
    /// </summary>
    public const int DefaultTake = 50;

    /// <summary>
    ///     This constant contains the largest page size a caller may request.
    /// </summary>
    public const int MaxTake = 500;

    /// <summary>
    ///     This property contains how many items to skip before the returned page begins.
    /// </summary>
    public int Skip { get; set; }

    /// <summary>
    ///     This property contains the maximum number of items to return.
    /// </summary>
    public int Take { get; set; } = DefaultTake;

    /// <summary>
    ///     This method clamps the values to a safe range instead of rejecting them, as most table
    ///     paging controls do with a hand-edited URL. A negative <see cref="Skip" /> becomes 0, a
    ///     <see cref="Take" /> of zero or less becomes <see cref="DefaultTake" />, and one above
    ///     <see cref="MaxTake" /> becomes <see cref="MaxTake" />.
    /// </summary>
    public (int Skip, int Take) Normalize() =>
        (Math.Max(0, Skip), Math.Clamp(Take <= 0 ? DefaultTake : Take, 1, MaxTake));
}
