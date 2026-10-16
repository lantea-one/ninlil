using System.Xml.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Model;

/// <summary>
///     This class is a fluent list that keeps at most one item per value of a chosen property (the
///     "distinct property"), such as a list of records keyed by name. Adding an item replaces any
///     existing item with the same distinct value. It is serialized as a plain list.
/// </summary>
/// <typeparam name="TType">The type of elements in the list.</typeparam>
/// <typeparam name="TMatcher">The type of the property that decides whether two items are the same.</typeparam>
[XmlInclude(typeof(FluentListModel<>))]
[XmlRoot("list")]
public class FluentDistinctListModel<TType, TMatcher>
    : FluentListModel<TType>,
        IFluentDistinctListModel<TType, TMatcher>
{
    /// <summary>
    ///     This method keeps the first item for each distinct property value in
    ///     <paramref name="source" />.
    /// </summary>
    /// <param name="source">The items to filter.</param>
    /// <param name="distinctPropertySelector">The function that selects each item's distinct property.</param>
    /// <returns>The first item for each distinct value, in source order.</returns>
    public static IEnumerable<TType> Distinct(
        IEnumerable<TType> source,
        Func<TType, TMatcher> distinctPropertySelector
    ) => source.GroupBy(distinctPropertySelector).Select(g => g.First());

    /// <summary>
    ///     This field holds the function that selects each item's distinct property.
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
    ///     class with the specified distinct property selector and initial <paramref name="collection" />
    ///     of items. Duplicates by distinct property are dropped, keeping the first.
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
    ///     Duplicates by distinct property are dropped, keeping the first.
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
    ///     This method fluently adds <paramref name="item" />, first removing any existing item with
    ///     the same distinct property value. It overrides <see cref="FluentListModel{TType}.WithItem(TType)" />,
    ///     which compares whole items instead.
    /// </summary>
    /// <param name="item">The item to be added to the list.</param>
    /// <returns>
    ///     The current instance of <see cref="FluentDistinctListModel{TType, TMatcher}" /> after
    ///     adding the specified item, allowing for method chaining.
    /// </returns>
    public override FluentDistinctListModel<TType, TMatcher> WithItem(TType item)
    {
        // A null item has no distinct property value, so use the base handling, which ignores a
        // null item and returns this instance (making the cast safe).
        if (item is null)
            return (FluentDistinctListModel<TType, TMatcher>)base.WithItem(item);

        // Remove any existing item with the same distinct property value.
        WithoutItem(_distinctPropertySelector(item));

        // Add the item and return this instance for method chaining.
        Add(item);
        return this;
    }

    /// <summary>
    ///     This method fluently removes every item whose distinct property equals
    ///     <paramref name="matchValue" />. A null value matches items whose distinct property is null.
    /// </summary>
    /// <param name="matchValue">The distinct property value of the items to remove.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentDistinctListModel<TType, TMatcher> WithoutItem(TMatcher matchValue) =>
        base.WithoutItems(p =>
            EqualityComparer<TMatcher>.Default.Equals(_distinctPropertySelector(p), matchValue)
        ) as FluentDistinctListModel<TType, TMatcher>;

    /// <summary>
    ///     This method fluently removes every item whose distinct property is one of
    ///     <paramref name="matchValues" />.
    /// </summary>
    /// <param name="matchValues">The distinct property values of the items to remove.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentDistinctListModel<TType, TMatcher> WithoutItems(
        IEnumerable<TMatcher> matchValues
    ) =>
        base.WithoutItems(p => matchValues.Contains(_distinctPropertySelector(p)))
        as FluentDistinctListModel<TType, TMatcher>;

    /// <summary>
    ///     This method fluently removes every item whose distinct property is one of
    ///     <paramref name="matchValues" />.
    /// </summary>
    /// <param name="matchValues">The distinct property values of the items to remove.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentDistinctListModel<TType, TMatcher> WithoutItems(params TMatcher[] matchValues) =>
        WithoutItems(matchValues.AsEnumerable());
}
