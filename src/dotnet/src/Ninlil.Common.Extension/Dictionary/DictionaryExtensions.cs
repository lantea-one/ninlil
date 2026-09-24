using Ninlil.Common.Extension.Object;

// Define our namespace.
namespace Ninlil.Common.Extension.Dictionary;

/// <summary>
///     This class provides extension methods for the <see cref="Dictionary{TKey,TValue}" /> class.
/// </summary>
public static class DictionaryExtensions
{
    /// <summary>
    ///     This method merges the <paramref name="additions" /> into the <paramref name="source" />.
    /// </summary>
    /// <param name="source">The source dictionary in which to merge the <paramref name="additions" />.</param>
    /// <param name="additions">The dictionary to merge into the <paramref name="source" />.</param>
    /// <param name="overwrite">Whether to overwrite the <paramref name="source" /> value with the <paramref name="additions" /> value.</param>
    /// <typeparam name="TKey">The expected type of key in <paramref name="source" /> and <paramref name="additions" />.</typeparam>
    /// <typeparam name="TValue">The expected type of value in <paramref name="source" /> and <paramref name="additions" />.</typeparam>
    /// <returns>The merged <paramref name="source" /> and <paramref name="additions" /> dictionary.</returns>
    public static Dictionary<TKey, TValue> Merge<TKey, TValue>(
        this Dictionary<TKey, TValue> source,
        Dictionary<TKey, TValue> additions,
        bool overwrite = true
    )
    {
        // Iterate through the additions
        foreach (var (key, value) in additions)
            // Check the overwrite flag the overwrite the source value with the additions value
            if (overwrite)
                source[key] = value;
            // Otherwise, add the value to the source
            else
                source.Add(key, value);

        // We're done, return the source
        return source;
    }

    /// <summary>
    ///     This method converts the <paramref name="instance" /> to a string representation suitable for console output.
    /// </summary>
    /// <param name="instance">The instance of the <see cref="Dictionary{TKey,TValue}" /> to convert.</param>
    /// <typeparam name="TKey">The expected type of key in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TValue">The expected type of value in <paramref name="instance" />.</typeparam>
    /// <returns>The string representation of the <paramref name="instance" /> suitable for console output.</returns>
    public static string ToConsoleString<TKey, TValue>(this Dictionary<TKey, TValue> instance) =>
        instance?.ToStringValue() ?? "null";
}
