// Define our namespace.
namespace Ninlil.Common.Extension.Type;

/// <summary>
///     This class contains extension methods for <see cref="System.Type"/> to perform various operations on tuple types.
/// </summary>
public static class TupleTypeExtension
{
    /// <summary>
    ///     This method retrieves the <see cref="System.Type" /> for each of the items in a tuple,
    ///     and returns them as an array of <see cref="System.Type"/>, which is empty if the instance is not a tuple.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type"/> instance to check.</param>
    /// <returns>
    ///     An array of <see cref="System.Type"/> representing the types of
    ///     the tuple elements, or an empty array if the instance is not a tuple.
    /// </returns>
    public static System.Type[] GetTupleElementTypes(this System.Type instance) =>
        instance.IsTuple() ? instance.GenericTypeArguments : [];
}
