using System.Xml.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Model;

/// <summary>
///     This class is a <see cref="List{T}" /> with fluent add and remove methods that return the
///     list for chaining. <see cref="WithItem(TType)" /> removes an equal item before adding, so
///     the list avoids duplicates by <see cref="object.Equals(object)" />.
///     <see cref="FluentDistinctListModel{TType, TMatcher}" /> builds on it to deduplicate by a
///     chosen property instead. It serializes as a plain XML <c>list</c>.
/// </summary>
/// <typeparam name="TType">The type of items in the list.</typeparam>
[XmlInclude(typeof(List<>))]
[XmlRoot("list")]
public class FluentListModel<TType> : List<TType>, IFluentListModel<TType>
{
    /// <summary>
    ///     This constructor initializes a new, empty instance of the <see cref="FluentListModel{TType}" /> class.
    /// </summary>
    public FluentListModel() { }

    /// <summary>
    ///     This constructor initializes a new, empty instance of the <see cref="FluentListModel{TType}" />
    ///     class with the specified initial <paramref name="capacity" />.
    /// </summary>
    /// <param name="capacity">The initial capacity of the list.</param>
    public FluentListModel(int capacity)
        : base(capacity) { }

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="FluentListModel{TType}" />
    ///     class with the specified initial <paramref name="collection" /> of items.
    /// </summary>
    /// <param name="collection">The initial collection of items to be added to the list.</param>
    public FluentListModel(IEnumerable<TType> collection)
        : base(collection) { }

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="FluentListModel{TType}" />
    ///     class with the specified initial <paramref name="items" />.
    /// </summary>
    /// <param name="items">The initial items to be added to the list.</param>
    public FluentListModel(params TType[] items)
        : base(items) { }

    /// <summary>
    ///     This method removes every item from the list.
    /// </summary>
    /// <returns>The current list, for method chaining.</returns>
    public FluentListModel<TType> ClearItems()
    {
        // Clear all items from the list.
        this.Clear();

        // We're done, return the updated instance of the FluentListModel for method chaining.
        return this;
    }

    /// <summary>
    ///     This method adds <paramref name="item" />, first removing one existing item equal to it.
    /// </summary>
    /// <param name="item">The item to be added to the list.</param>
    /// <returns>The current list, for method chaining.</returns>
    public virtual FluentListModel<TType> WithItem(TType item)
    {
        // Remove an existing equal item (by Equals) so the item is not duplicated.
        // FluentDistinctListModel overrides this to compare a chosen property instead.
        WithoutItem(item);

        // Add the provided item to the list.
        this.Add(item);

        // We're done, return the updated instance of the FluentListModel for method chaining.
        return this;
    }

    /// <summary>
    ///     This method adds each of <paramref name="items" /> through <see cref="WithItem(TType)" />, so
    ///     duplicates are handled the same way (including by an override in a derived class).
    /// </summary>
    /// <param name="items">The collection of items to be added to the list.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentListModel<TType> WithItems(IEnumerable<TType> items)
    {
        // Iterate through the provided collection of items and add each one to the list.
        foreach (var item in items)
            WithItem(item);

        // We're done, return the updated instance of the FluentListModel for method chaining.
        return this;
    }

    /// <summary>
    ///     This method adds each of <paramref name="items" />; see
    ///     <see cref="WithItems(IEnumerable{TType})" />.
    /// </summary>
    /// <param name="items">A variable number of items to be added to the list.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentListModel<TType> WithItems(params TType[] items) =>
        WithItems(items.AsEnumerable());

    /// <summary>
    ///     This method removes the first item equal to <paramref name="item" />, if any. A null item
    ///     is ignored.
    /// </summary>
    /// <param name="item">The item to be removed from the list.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentListModel<TType> WithoutItem(TType item)
    {
        // A null item is ignored.
        if (item is null)
            return this;

        // Remove the specified item from the list.
        this.Remove(item);

        // We're done, return the updated instance of the FluentListModel for method chaining.
        return this;
    }

    /// <summary>
    ///     This method removes the first item that matches <paramref name="predicate" />. With no
    ///     match, the list is unchanged.
    /// </summary>
    /// <param name="predicate">The condition the item to remove must meet.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentListModel<TType> WithoutItem(Func<TType, bool> predicate)
    {
        // Remove by index: a match search that falls back to default(TType) would remove a real
        // default value (such as 0) when nothing matches.
        int index = FindIndex(item => predicate(item));
        if (index >= 0)
            RemoveAt(index);

        // We're done, return the updated instance for method chaining.
        return this;
    }

    /// <summary>
    ///     This method removes one equal item for each of <paramref name="items" />.
    /// </summary>
    /// <param name="items">The collection of items to be removed from the list.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentListModel<TType> WithoutItems(IEnumerable<TType> items)
    {
        // Iterate through the provided collection of items and remove each one from the list.
        foreach (var item in items)
            WithoutItem(item);

        // We're done, return the updated instance of the FluentListModel for method chaining.
        return this;
    }

    /// <summary>
    ///     This method removes one equal item for each of <paramref name="items" />.
    /// </summary>
    /// <param name="items">A variable number of items to be removed from the list.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentListModel<TType> WithoutItems(params TType[] items) =>
        WithoutItems(items.AsEnumerable());

    /// <summary>
    ///     This method removes every item that matches <paramref name="predicate" />.
    /// </summary>
    /// <param name="predicate">The condition the items to remove must meet.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentListModel<TType> WithoutItems(Func<TType, bool> predicate) =>
        // Materialize the matches with ToList() first. WithoutItems(IEnumerable) removes items
        // while enumerating its argument, and a lazy query over this list would throw
        // InvalidOperationException after the first removal.
        WithoutItems(this.Where(predicate).ToList());
}
