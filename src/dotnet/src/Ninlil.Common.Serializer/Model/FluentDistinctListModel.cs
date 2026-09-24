using System.Xml.Serialization;

// Define our namespace for the FluentDistinctListModel class, which provides a generic
// list implementation that ensures distinct elements based on a specified property.
namespace Ninlil.Common.Serializer.Model;

/// <summary>
///     Represents a list that ensures distinct elements based on a specified property.
/// </summary>
/// <typeparam name="TType">
///     The type of elements in the list. This type parameter allows the
///     list to hold any type of object, providing flexibility in its usage.
/// </typeparam>
/// <typeparam name="TMatcher">
///     The type of the property used to determine distinctness. This type parameter
///     allows the list to compare elements based on a specific property, ensuring
///     that only unique elements are retained according to the selected property.
/// </typeparam>
[XmlInclude(typeof(FluentListModel<>))]
[XmlRoot("list")]
public class FluentDistinctListModel<TType, TMatcher>
    : FluentListModel<TType>,
        IFluentDistinctListModel<TType, TMatcher>
{
    /// <summary>
    ///     This factory method creates a distinct list from the provided <paramref name="source" /> collection,
    ///     using the specified <paramref name="distinctPropertySelector" /> to determine uniqueness. It returns
    ///     an <see cref="IEnumerable{TType}" /> containing only the distinct elements based on the selected property.
    ///     This method is useful for filtering out duplicate items from a collection according to a specific property
    ///     or criteria, allowing for more efficient data handling and manipulation.
    /// </summary>
    /// <param name="source">
    ///     The source collection from which to create the distinct list. It is an
    ///     <see cref="IEnumerable{TType}" /> containing the elements to be filtered
    ///     for uniqueness.
    /// </param>
    /// <param name="distinctPropertySelector">
    ///     A function that selects the property of each element in the source collection to be used for determining
    ///     distinctness. It is a delegate that takes an element of type <typeparamref name="TType" /> and returns a
    ///     value of type <typeparamref name="TMatcher" /> representing the property to be used for comparison.
    /// </param>
    /// <returns>
    ///     An <see cref="IEnumerable{TType}" /> containing only the distinct elements based on the selected property.
    /// </returns>
    public static IEnumerable<TType> Distinct(
        IEnumerable<TType> source,
        Func<TType, TMatcher> distinctPropertySelector
    ) => source.GroupBy(distinctPropertySelector).Select(g => g.First());

    /// <summary>
    ///     This field holds the predicate function used to determine the distinctness
    ///     of elements in the list. It is a delegate that takes an element of type
    ///     <typeparamref name="TType" /> and returns a value of type
    ///     <typeparamref name="TMatcher" /> indicating the distinct property of the
    ///     element. This selector is used to filter out duplicate elements when adding
    ///     new items to the list, ensuring that only unique elements are retained according
    ///     to the specified logic.
    /// </summary>
    private readonly Func<TType, TMatcher> _distinctPropertySelector;

    /// <summary>
    ///     This constructor initializes a new instance of
    ///     the <see cref="FluentDistinctListModel{TType, TMatcher}" />
    ///     class with the specified distinct property selector.
    /// </summary>
    /// <param name="distinctPropertySelector">
    ///     The function used to select the distinct property of elements in the list.
    /// </param>
    public FluentDistinctListModel(Func<TType, TMatcher> distinctPropertySelector) =>
        _distinctPropertySelector = distinctPropertySelector;

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="FluentDistinctListModel{TType, TMatcher}" />
    ///     class with the specified distinct property selector and initial <paramref name="capacity" />.
    /// </summary>
    /// <param name="distinctPropertySelector">
    ///     The function used to select the distinct property of elements in the list.
    /// </param>
    /// <param name="capacity">The initial capacity of the list.</param>
    public FluentDistinctListModel(Func<TType, TMatcher> distinctPropertySelector, int capacity)
        : base(capacity) => _distinctPropertySelector = distinctPropertySelector;

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="FluentDistinctListModel{TType, TMatcher}" />
    ///     class with the specified distinct property selector and initial <paramref name="collection" /> of items.
    /// </summary>
    /// <param name="distinctPropertySelector">
    ///     The function used to select the distinct property of elements in the list.
    /// </param>
    /// <param name="collection">The initial collection of items to be added to the list.</param>
    public FluentDistinctListModel(
        Func<TType, TMatcher> distinctPropertySelector,
        IEnumerable<TType> collection
    )
        : base(Distinct(collection, distinctPropertySelector)) =>
        _distinctPropertySelector = distinctPropertySelector;

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="FluentDistinctListModel{TType, TMatcher}" />
    ///     class with the specified distinct property selector and initial <paramref name="items" />.
    /// </summary>
    /// <param name="distinctPropertySelector">
    ///     The function used to select the distinct property of elements in the list.
    /// </param>
    /// <param name="items">The initial items to be added to the list.</param>
    public FluentDistinctListModel(
        Func<TType, TMatcher> distinctPropertySelector,
        params TType[] items
    )
        : this(distinctPropertySelector, Distinct(items, distinctPropertySelector)) { }

    /// <summary>
    ///     This method fluently adds a new <paramref name="item" /> to the list, overriding the base
    ///     <see cref="FluentListModel{TType}.WithItem(TType)" /> to enforce distinctness by the
    ///     configured distinct property selector rather than by the base class's default equality
    ///     comparison -- any existing item that shares this item's distinct property value is
    ///     removed first, so the list never ends up holding two items with the same distinct
    ///     property value even if they are otherwise unequal.
    /// </summary>
    /// <param name="item">The item to be added to the list.</param>
    /// <returns>
    ///     The current instance of <see cref="FluentDistinctListModel{TType, TMatcher}" /> after
    ///     adding the specified item, allowing for method chaining.
    /// </returns>
    public override FluentDistinctListModel<TType, TMatcher> WithItem(TType item)
    {
        // A null item has no distinct property value to compare against, so fall back to the
        // base class's own equality-based handling (which itself no-ops on a null item, and still
        // returns this same instance, so the cast below is safe).
        if (item is null)
            return (FluentDistinctListModel<TType, TMatcher>)base.WithItem(item);

        // Remove any existing item that shares this item's distinct property value before adding,
        // so distinctness is enforced by the selected property rather than by object equality.
        WithoutItem(_distinctPropertySelector(item));

        // Add the item and return this instance for method chaining.
        Add(item);
        return this;
    }

    /// <summary>
    ///     This method fluently removes an item from the list based on the specified <paramref name="matchValue" />.
    ///     It uses the distinct property selector to identify the item to be removed, ensuring that only the item
    ///     with the matching distinct property value is removed from the list. The method returns the current
    ///     instance of <see cref="FluentDistinctListModel{TType, TMatcher}" /> after the removal operation, allowing
    ///     for method chaining and further modifications to the list.
    /// </summary>
    /// <param name="matchValue">
    ///     The value of the distinct property used to identify the item to be removed from the list. This value
    ///     is compared against the distinct property of each item in the list to determine which item should be
    ///     removed. If an item with a matching distinct property value is found, it will be removed from the list,
    ///     and the method will return the current instance of <see cref="FluentDistinctListModel{TType, TMatcher}" />
    ///     for further modifications or method chaining.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="FluentDistinctListModel{TType, TMatcher}" /> after
    ///     the removal operation, allowing for method chaining and further modifications to the list.
    /// </returns>
    public FluentDistinctListModel<TType, TMatcher> WithoutItem(TMatcher matchValue) =>
        base.WithoutItems(p => _distinctPropertySelector(p).Equals(matchValue))
        as FluentDistinctListModel<TType, TMatcher>;

    /// <summary>
    ///     This method fluently removes multiple items from the list based on the specified
    ///     <paramref name="matchValues" />. It uses the distinct property selector to identify the items
    ///     to be removed, ensuring that only the items with matching distinct property values are removed
    ///     from the list. The method returns the current instance of
    ///     <see cref="FluentDistinctListModel{TType, TMatcher}" /> after the removal operation, allowing
    ///     for method chaining and further modifications to the list.
    /// </summary>
    /// <param name="matchValues">
    ///     A collection of values of the distinct property used to identify the items to be removed from the
    ///     list. These values are compared against the distinct property of each item in the list to determine
    ///     which items should be removed. If any items with matching distinct property values are found, they
    ///     will be removed from the list, and the method will return the current instance of
    ///     <see cref="FluentDistinctListModel{TType, TMatcher}" /> for further modifications or method chaining.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="FluentDistinctListModel{TType, TMatcher}" /> after
    ///     the removal operation, allowing for method chaining and further modifications to the list.
    /// </returns>
    public FluentDistinctListModel<TType, TMatcher> WithoutItems(
        IEnumerable<TMatcher> matchValues
    ) =>
        base.WithoutItems(p => matchValues.Contains(_distinctPropertySelector(p)))
        as FluentDistinctListModel<TType, TMatcher>;

    /// <summary>
    ///     This method fluently removes multiple items from the list based on the specified
    ///     <paramref name="matchValues" />. It uses the distinct property selector to identify
    ///     the items to be removed, ensuring that only the items with matching distinct property
    ///     values are removed from the list. The method returns the current instance of
    ///     <see cref="FluentDistinctListModel{TType, TMatcher}" /> after the removal operation,
    ///     allowing for method chaining and further modifications to the list.
    /// </summary>
    /// <param name="matchValues">
    ///     A variable number of values of the distinct property used to identify the items to be removed
    ///     from the list. These values are compared against the distinct property of each item in the list
    ///     to determine which items should be removed. If any items with matching distinct property values
    ///     are found, they will be removed from the list, and the method will return the current instance
    ///     of <see cref="FluentDistinctListModel{TType, TMatcher}" /> for further modifications or method
    ///     chaining.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="FluentDistinctListModel{TType, TMatcher}" /> after
    ///     the removal operation, allowing for method chaining and further modifications to the list.
    /// </returns>
    public FluentDistinctListModel<TType, TMatcher> WithoutItems(params TMatcher[] matchValues) =>
        WithoutItems(matchValues.AsEnumerable());
}
