// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class wraps a single page of a larger list -- the shape every list endpoint in this
///     project's REST API returns, offset/limit paginated (a sane default/max page size is enough
///     at this project's own homelab/small-org target scale; a cursor-based scheme's extra
///     complexity is not needed).
/// </summary>
/// <param name="Items">The page's own items.</param>
/// <param name="Skip">How many items were skipped before this page began.</param>
/// <param name="Take">The maximum number of items this page could contain.</param>
/// <param name="TotalCount">The total number of items across every page.</param>
/// <typeparam name="TItem">The type of item this page carries.</typeparam>
public record PagedResult<TItem>(IReadOnlyList<TItem> Items, int Skip, int Take, int TotalCount);

/// <summary>
///     This class carries the offset/limit pagination parameters every list endpoint accepts as
///     query-string parameters.
/// </summary>
public class PaginationQuery
{
    /// <summary>
    ///     This property contains the default page size used when a caller does not specify one.
    /// </summary>
    public const int DefaultTake = 50;

    /// <summary>
    ///     This property contains the largest page size a caller may request.
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
    ///     This method clamps this query's own values to a safe range -- a negative
    ///     <see cref="Skip" /> or a <see cref="Take" /> of zero, negative, or larger than
    ///     <see cref="MaxTake" /> is silently corrected rather than rejected outright, matching
    ///     how most admin-UI table paging controls behave when driven by a hand-edited URL.
    /// </summary>
    public (int Skip, int Take) Normalize() =>
        (Math.Max(0, Skip), Math.Clamp(Take <= 0 ? DefaultTake : Take, 1, MaxTake));
}
