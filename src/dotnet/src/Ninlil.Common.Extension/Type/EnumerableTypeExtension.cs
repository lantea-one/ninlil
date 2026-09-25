// Define our namespace.
namespace Ninlil.Common.Extension.Type;

/// <summary>
///     This static class reads the element type of an array or generic collection
///     <see cref="System.Type" />. The string conversion helpers use it to convert each item.
/// </summary>
public static class EnumerableTypeExtension
{
    /// <summary>
    ///     This method returns the element <see cref="System.Type" /> of an <see cref="Array" />,
    ///     <see cref="ICollection{T}" />, <see cref="IEnumerable{T}" /> or <see cref="IList{T}" />
    ///     type: the array element type, or else the first generic argument. It returns
    ///     <see langword="null" /> for any other type.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type"/> instance to check.</param>
    /// <returns>
    ///     The <see cref="System.Type"/> of the element if the instance is an enumerable type, otherwise <see langword="null" />.
    /// </returns>
    public static System.Type GetEnumerableElementType(this System.Type instance) =>
        !instance.IsEnumerableType(false)
            ? null
            : instance.GetElementType() ?? instance.GenericTypeArguments.ElementAtOrDefault(0);
}
