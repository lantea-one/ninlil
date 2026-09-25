// Define our namespace.
namespace Ninlil.Common.Serializer.Model;

/// <summary>
///     This interface is the contract for <see cref="FluentListModel{TType}" />: a list with fluent
///     add and remove methods that return the list for chaining.
/// </summary>
/// <typeparam name="TType">The type of items in the list.</typeparam>
public interface IFluentListModel<TType> : IList<TType>
{
    /// <summary>
    ///     This method removes every item from the list.
    /// </summary>
    /// <returns>The current list, for method chaining.</returns>
    public FluentListModel<TType> ClearItems();

    /// <summary>
    ///     This method adds <paramref name="item" />, first removing a duplicate of it.
    /// </summary>
    /// <param name="item">The item to be added to the list.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentListModel<TType> WithItem(TType item);

    /// <summary>
    ///     This method adds each of <paramref name="items" /> as <see cref="WithItem(TType)" /> does.
    /// </summary>
    /// <param name="items">The collection of items to be added to the list.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentListModel<TType> WithItems(IEnumerable<TType> items);

    /// <summary>
    ///     This method adds each of <paramref name="items" /> as <see cref="WithItem(TType)" /> does.
    /// </summary>
    /// <param name="items">A variable number of items to be added to the list.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentListModel<TType> WithItems(params TType[] items);

    /// <summary>
    ///     This method removes the first item equal to <paramref name="item" />, if any.
    /// </summary>
    /// <param name="item">The item to be removed from the list.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentListModel<TType> WithoutItem(TType item);

    /// <summary>
    ///     This method removes the first item that matches <paramref name="predicate" />.
    /// </summary>
    /// <param name="predicate">The condition the item to remove must meet.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentListModel<TType> WithoutItem(Func<TType, bool> predicate);

    /// <summary>
    ///     This method removes one equal item for each of <paramref name="items" />.
    /// </summary>
    /// <param name="items">The collection of items to be removed from the list.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentListModel<TType> WithoutItems(IEnumerable<TType> items);

    /// <summary>
    ///     This method removes one equal item for each of <paramref name="items" />.
    /// </summary>
    /// <param name="items">A variable number of items to be removed from the list.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentListModel<TType> WithoutItems(params TType[] items);

    /// <summary>
    ///     This method removes every item that matches <paramref name="predicate" />.
    /// </summary>
    /// <param name="predicate">The condition the items to remove must meet.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentListModel<TType> WithoutItems(Func<TType, bool> predicate);
}
