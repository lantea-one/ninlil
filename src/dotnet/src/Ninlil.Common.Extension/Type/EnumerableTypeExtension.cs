// Define our namespace.
namespace Ninlil.Common.Extension.Type;

/// <summary>
///     This class contains extension methods for <see cref="System.Type"/> to
///     perform various operations on enumerable types.
/// </summary>
public static class EnumerableTypeExtension
{
    /// <summary>
    ///     This method return the <see cref="System.Type" /> of the element in an
    ///     <see cref="Array" />, <see cref="ICollection{T}" />, <see cref="IEnumerable{T}" />
    ///     or <see cref="IList{T}" /> type, by default <see langword="null" /> is returned.
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
