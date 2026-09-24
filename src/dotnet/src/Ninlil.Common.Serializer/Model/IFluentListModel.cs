// Define our namespace for the IFluentListModel interface.
namespace Ninlil.Common.Serializer.Model;

/// <summary>
///     This interface represents a generic list model that provides fluent methods for adding and removing items.
///     It inherits from the generic <see cref="IList{TType}" /> interface, where TType is the type of items in the
///     list. The interface provides fluent methods for adding and removing items, allowing for method chaining.
///     This interface is useful for scenarios where you want to build and manipulate a list of items in a fluent
///     and expressive manner.
/// </summary>
/// <typeparam name="TType">
///     The type of items in the list. This type parameter allows the list
///     to hold any type of object, providing flexibility in its usage.
/// </typeparam>
public interface IFluentListModel<TType> : IList<TType>
{
    /// <summary>
    ///     This method fluently clears all items from the <see cref="FluentListModel{TType}" />.
    ///     It removes all elements from the list, effectively resetting it to an empty state.
    ///     This method is useful when you want to start fresh with an empty list and remove
    ///     all previously added items. After calling this method, the list will be empty,
    ///     and you can add new items as needed. The method returns the current instance of
    ///     <see cref="FluentListModel{TType}" /> to allow for method chaining, enabling
    ///     you to perform additional operations on the list after clearing it.
    /// </summary>
    /// <returns>
    ///     The current instance of <see cref="FluentListModel{TType}" />
    ///     after clearing all items. This allows for method chaining, enabling you
    ///     to perform additional operations on the list after clearing it.
    /// </returns>
    public FluentListModel<TType> ClearItems();

    /// <summary>
    ///     This method fluently adds a new <paramref name="item" /> to the
    ///     <see cref="FluentListModel{TType}" />. It adds the specified item to the list
    ///     and returns the current instance of the list for method chaining. This method is useful
    ///     when you want to add items to the list in a fluent manner, allowing you to chain multiple
    ///     method calls together. After calling this method, the specified item will be added to the
    ///     list, and you can continue to add more items or perform other operations on the list. The
    ///     method returns the current instance of <see cref="FluentListModel{TType}" /> to
    ///     allow for method chaining, enabling you to perform additional operations on the list
    ///     after adding the item.
    /// </summary>
    /// <param name="item">The item to be added to the list.</param>
    /// <returns>
    ///     The current instance of <see cref="FluentListModel{TType}" />
    ///     after adding the specified item. This allows for method chaining, enabling you
    ///     to perform additional operations on the list after adding the item.
    /// </returns>
    public FluentListModel<TType> WithItem(TType item);

    /// <summary>
    ///     This method fluently adds a collection of new <paramref name="items" /> to the
    ///     <see cref="FluentListModel{TType}" />. It iterates through the provided
    ///     collection of items and adds each one to the list using the
    ///     <see cref="FluentListModel{TType}.WithItem(TType)" /> method. This ensures that
    ///     any existing items with the same distinct property value are removed before adding
    ///     the new items, preventing duplicates. The method returns the current instance of
    ///     <see cref="FluentListModel{TType}" /> to allow for method chaining, enabling you
    ///     to perform additional operations on the list after adding the items. This method
    ///     is useful when you want to add multiple items to the list in a fluent manner,
    ///     allowing you to chain multiple method calls together.
    /// </summary>
    /// <param name="items">The collection of items to be added to the list.</param>
    /// <returns>
    ///     The current instance of <see cref="FluentListModel{TType}" />
    ///     after adding the specified collection of items. This allows for method
    ///     chaining, enabling you to perform additional operations on the list after
    ///     adding the items.
    /// </returns>
    public FluentListModel<TType> WithItems(IEnumerable<TType> items);

    /// <summary>
    ///     This method fluently adds a variable number of new <paramref name="items" /> to the
    ///     <see cref="FluentListModel{TType}" />. It converts the provided array of items
    ///     into an enumerable collection and calls the <see cref="WithItems(IEnumerable{TType})" />
    ///     method to add each item to the list. This ensures that any existing items with the same
    ///     distinct property value are removed before adding the new items, preventing duplicates.
    ///     The method returns the current instance of <see cref="FluentListModel{TType}" />
    ///     to allow for method chaining, enabling you to perform additional operations on the list
    ///     after adding the items. This method is useful when you want to add multiple items to the
    ///     list in a fluent manner, allowing you to chain multiple method calls together.
    /// </summary>
    /// <param name="items">A variable number of items to be added to the list.</param>
    /// <returns>
    ///     The current instance of <see cref="FluentListModel{TType}" />
    ///     after adding the specified variable number of items. This allows for method
    ///     chaining, enabling you to perform additional operations on the list after
    ///     adding the items.
    /// </returns>
    public FluentListModel<TType> WithItems(params TType[] items);

    /// <summary>
    ///     This method fluently removes a specified <paramref name="item" /> from the
    ///     <see cref="FluentListModel{TType}" />. It removes the specified item from
    ///     the list and returns the current instance of the list for method chaining.
    ///     This method is useful when you want to remove items from the list in a fluent
    ///     manner, allowing you to chain multiple method calls together. After calling
    ///     this method, the specified item will be removed from the list, and you can
    ///     continue to remove more items or perform other operations on the list. The
    ///     method returns the current instance of <see cref="FluentListModel{TType}" />
    ///     to allow for method chaining, enabling you to perform additional operations
    ///     on the list after removing the item.
    /// </summary>
    /// <param name="item">
    ///     The item to be removed from the list. This parameter specifies the item that should
    ///     be removed from the <see cref="FluentListModel{TType}" />. If the item is found in
    ///     the list, it will be removed; otherwise, no action will be taken. The method ensures
    ///     that the specified item is removed from the list, allowing for fluent manipulation
    ///     of the collection.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="FluentListModel{TType}" />
    ///     after removing the specified item. This allows for method chaining, enabling you
    ///     to perform additional operations on the list after removing the item. If the
    ///     specified item was not found in the list, the method will still return the current
    ///     instance, allowing for continued fluent manipulation of the collection without interruption.
    /// </returns>
    public FluentListModel<TType> WithoutItem(TType item);

    /// <summary>
    ///     This method fluently removes the first item from the <see cref="FluentListModel{TType}" />
    ///     that matches the specified <paramref name="predicate" />. It searches for the first item in
    ///     the list that satisfies the provided predicate and removes it from the list. If no matching
    ///     list, the method returns the current instance of <see cref="FluentListModel{TType}" /> for method chaining,
    ///     allowing you to continue manipulating the list in a fluent manner. This method is useful when you want to
    ///     remove items from the list based on specific conditions defined by the predicate, enabling more flexible
    ///     and dynamic list manipulation.
    /// </summary>
    /// <param name="predicate">
    ///     A function that defines the conditions of the item to be removed. This predicate is used to
    ///     identify the first item in the list that matches the specified criteria. The method will search
    ///     through the list and remove the first item that satisfies the predicate. If no item matches the
    ///     predicate, the method will not remove any items and will simply return the current instance of the list.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="FluentListModel{TType}" /> after attempting to remove the
    ///     first item that matches the specified predicate. This allows for method chaining, enabling you
    ///     to perform additional operations on the list after the removal attempt. If no matching item was
    ///     found, the method will still return the current instance, allowing for continued fluent manipulation
    ///     of the collection without interruption.
    /// </returns>
    public FluentListModel<TType> WithoutItem(Func<TType, bool> predicate);

    /// <summary>
    ///     This method fluently removes a collection of specified <paramref name="items" /> from the
    ///     <see cref="FluentListModel{TType}" />. It iterates through the provided collection of items
    ///     and removes each one from the list using the <see cref="WithoutItem(TType)" /> method. This
    ///     ensures that each specified item is removed from the list, and the method returns the current
    ///     instance of <see cref="FluentListModel{TType}" /> to allow for method chaining, enabling you
    ///     to perform additional operations on the list after removing the items. This method is useful
    ///     when you want to remove multiple items from the list in a fluent manner, allowing you to chain
    ///     multiple method calls together. After calling this method, the specified items will be removed
    ///     from the list, and you can continue to remove more items or perform other operations on the list.
    /// </summary>
    /// <param name="items">
    ///     The collection of items to be removed from the list. This parameter specifies the items that
    ///     should be removed from the <see cref="FluentListModel{TType}" />. The method will iterate through
    ///     the provided collection and remove each item from the list using the <see cref="WithoutItem(TType)" />
    ///     method. If an item in the collection is not found in the list, no action will be taken for that item,
    ///     and the method will continue to process the remaining items in the collection. The method ensures
    ///     that all specified items are removed from the list, allowing for fluent manipulation of the collection.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="FluentListModel{TType}" /> after removing the specified
    ///     collection of items. This allows for method chaining, enabling you to perform additional
    ///     operations on the list after removing the items. If any of the specified items were not found
    ///     in the list, the method will still return the current instance, allowing for continued fluent
    ///     manipulation of the collection without interruption.
    /// </returns>
    public FluentListModel<TType> WithoutItems(IEnumerable<TType> items);

    /// <summary>
    ///     This method fluently removes a variable number of specified <paramref name="items" /> from the
    ///     <see cref="FluentListModel{TType}" />. It converts the provided array of items into an enumerable
    ///     collection and calls the <see cref="WithoutItems(IEnumerable{TType})" /> method to remove each
    ///     item from the list. This ensures that each specified item is removed from the list, and the method
    ///     returns the current instance of <see cref="FluentListModel{TType}" /> to allow for method chaining,
    ///     enabling you to perform additional operations on the list after removing the items. This method is
    ///     useful when you want to remove multiple items from the list in a fluent manner, allowing you to
    ///     chain multiple method calls together. After calling this method, the specified items will be removed
    ///     from the list, and you can continue to remove more items or perform other operations on the list.
    /// </summary>
    /// <param name="items">A variable number of items to be removed from the list.</param>
    /// <returns>
    ///     The current instance of <see cref="FluentListModel{TType}" /> after removing the specified variable
    ///     number of items. This allows for method chaining, enabling you to perform additional operations
    ///     on the list after removing the items. If any of the specified items were not found in the list,
    ///     the method will still return the current instance, allowing for continued fluent manipulation
    ///     of the collection without interruption.
    /// </returns>
    public FluentListModel<TType> WithoutItems(params TType[] items);

    /// <summary>
    ///     This method fluently removes all items from the <see cref="FluentListModel{TType}" /> that match
    ///     the specified <paramref name="predicate" />. It iterates through the list and removes each item
    ///     that satisfies the provided predicate. If no matching items are found, no action is taken. The
    ///     method returns the current instance of <see cref="FluentListModel{TType}" /> for method chaining,
    ///     allowing you to continue manipulating the list in a fluent manner. This method is useful when you
    ///     want to remove items from the list based on specific conditions defined by the predicate, enabling
    ///     more flexible and dynamic list manipulation.
    /// </summary>
    /// <param name="predicate">
    ///     A function that defines the conditions of the items to be removed. This predicate is used to identify
    ///     the items in the list that match the specified criteria. The method will iterate through the list and
    ///     remove all items that satisfy the predicate. If no items match the predicate, the method will not remove
    ///     any items and will simply return the current instance of the list.
    /// </param>
    /// <returns>The current instance of <see cref="FluentListModel{TType}" /> after attempting to remove all
    ///     items that match the specified predicate. This allows for method chaining, enabling you to perform
    ///     additional operations on the list after the removal attempt. If no matching items were found, the
    ///     method will still return the current instance, allowing for continued fluent manipulation of the
    ///     collection without interruption.
    /// </returns>
    public FluentListModel<TType> WithoutItems(Func<TType, bool> predicate);
}
