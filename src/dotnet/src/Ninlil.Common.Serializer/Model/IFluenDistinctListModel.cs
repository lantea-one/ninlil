// Define our namespace for the IFluentDistinctListModel interface, which provides a generic
// list implementation that ensures distinct elements based on a specified property.
namespace Ninlil.Common.Serializer.Model;

/// <summary>
///     This interface defines a fluent list model that ensures distinct elements based on a specified property.
///     It extends the <see cref="IFluentListModel{TType}" /> interface, providing additional methods for removing
///     items from the list based on a distinct property value. The interface is generic, allowing for flexibility
///     in the types of elements it can hold and the property used to determine distinctness. It is designed to
///     facilitate method chaining, enabling developers to perform multiple operations on the list in a fluent manner.
///     The interface includes methods for removing single or multiple items from the list based on their distinct
///     property values, allowing for fine-grained control over the contents of the list. This interface is intended
///     to be implemented by classes that provide a concrete implementation of a fluent distinct list model, ensuring
///     that only unique elements are retained according to the specified property.
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
public interface IFluentDistinctListModel<TType, TMatcher> : IFluentListModel<TType>
{
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
    public FluentDistinctListModel<TType, TMatcher> WithoutItem(TMatcher matchValue);

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
    public FluentDistinctListModel<TType, TMatcher> WithoutItems(IEnumerable<TMatcher> matchValues);

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
    public FluentDistinctListModel<TType, TMatcher> WithoutItems(params TMatcher[] matchValues);
}
