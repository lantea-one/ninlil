// Define our namespace.
namespace Ninlil.Common.Extension.Type;

/// <summary>
///     This static class reads the key and value types of a dictionary <see cref="System.Type" />
///     (as recognized by <see cref="IsTypeExtension.IsDictionary(System.Type)" />). The string conversion helpers
///     use it to convert each entry.
/// </summary>
public static class DictionaryTypeExtension
{
    /// <summary>
    ///     This method retrieves the <see cref="System.Type" />
    ///     of the key in a <see cref="Dictionary{TKey,TValue}"/>,
    ///     by default <see langword="null" /> is returned.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type"/> instance to check.</param>
    /// <returns>
    ///     The <see cref="System.Type"/> of the key if the instance is a dictionary, otherwise <see langword="null" />.
    /// </returns>
    public static System.Type GetDictionaryKeyType(this System.Type instance) =>
        instance.IsDictionary() ? instance.GenericTypeArguments.ElementAtOrDefault(0) : null;

    /// <summary>
    ///     This method retrieves the <see cref="System.Type" />
    ///     of the value in a <see cref="Dictionary{TKey,TValue}"/>,
    ///     by default <see langword="null" /> is returned.
    /// </summary>
    /// <param name="instance">The <see cref="System.Type"/> instance to check.</param>
    /// <returns>
    ///     The <see cref="System.Type"/> of the value if the instance is a dictionary, otherwise <see langword="null" />.
    /// </returns>
    public static System.Type GetDictionaryValueType(this System.Type instance) =>
        instance.IsDictionary() ? instance.GenericTypeArguments.ElementAtOrDefault(1) : null;
}
