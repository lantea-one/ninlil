// Define our namespace.
namespace Ninlil.Common.Serializer.Model;

/// <summary>
///     This interface is a fluent list that keeps at most one item per value of a chosen property
///     (the "distinct property"). It adds removal by that property's value to
///     <see cref="IFluentListModel{TType}" />. The file is named IFluenDistinctListModel.cs; the
///     interface name is spelled correctly.
/// </summary>
/// <typeparam name="TType">The type of elements in the list.</typeparam>
/// <typeparam name="TMatcher">The type of the property that decides whether two items are the same.</typeparam>
public interface IFluentDistinctListModel<TType, TMatcher> : IFluentListModel<TType>
{
    /// <summary>
    ///     This method fluently removes every item whose distinct property equals
    ///     <paramref name="matchValue" />.
    /// </summary>
    /// <param name="matchValue">The distinct property value of the items to remove.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentDistinctListModel<TType, TMatcher> WithoutItem(TMatcher matchValue);

    /// <summary>
    ///     This method fluently removes every item whose distinct property is one of
    ///     <paramref name="matchValues" />.
    /// </summary>
    /// <param name="matchValues">The distinct property values of the items to remove.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentDistinctListModel<TType, TMatcher> WithoutItems(IEnumerable<TMatcher> matchValues);

    /// <summary>
    ///     This method fluently removes every item whose distinct property is one of
    ///     <paramref name="matchValues" />.
    /// </summary>
    /// <param name="matchValues">The distinct property values of the items to remove.</param>
    /// <returns>The current list, for method chaining.</returns>
    public FluentDistinctListModel<TType, TMatcher> WithoutItems(params TMatcher[] matchValues);
}
