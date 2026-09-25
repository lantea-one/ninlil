using Ninlil.Common.Extension.Object;

// Define our namespace.
namespace Ninlil.Common.Extension.Dictionary;

/// <summary>
///     This static class adds merging and console-friendly formatting to
///     <see cref="Dictionary{TKey,TValue}" />.
/// </summary>
public static class DictionaryExtensions
{
    /// <summary>
    ///     This method merges the <paramref name="additions" /> into the <paramref name="source" /> in
    ///     place.
    /// </summary>
    /// <param name="source">The source dictionary in which to merge the <paramref name="additions" />.</param>
    /// <param name="additions">The dictionary to merge into the <paramref name="source" />.</param>
    /// <param name="overwrite">
    ///     Whether a key in both dictionaries takes the <paramref name="additions" /> value. When
    ///     <see langword="false" />, a duplicate key throws <see cref="ArgumentException" />.
    /// </param>
    /// <typeparam name="TKey">The expected type of key in <paramref name="source" /> and <paramref name="additions" />.</typeparam>
    /// <typeparam name="TValue">The expected type of value in <paramref name="source" /> and <paramref name="additions" />.</typeparam>
    /// <returns>The merged <paramref name="source" /> and <paramref name="additions" /> dictionary.</returns>
    public static Dictionary<TKey, TValue> Merge<TKey, TValue>(
        this Dictionary<TKey, TValue> source,
        Dictionary<TKey, TValue> additions,
        bool overwrite = true
    )
    {
        // Iterate through the additions.
        foreach (var (key, value) in additions)
            // With overwrite on, the addition replaces any existing value.
            if (overwrite)
                source[key] = value;
            // Otherwise, add the value (Dictionary.Add throws on a duplicate key).
            else
                source.Add(key, value);

        // We're done, return the source.
        return source;
    }

    /// <summary>
    ///     This method converts the <paramref name="instance" /> to a string for console output using
    ///     <see cref="ToStringValueObjectExtension.ToStringValue" />, or <c>"null"</c> when it is null.
    /// </summary>
    /// <param name="instance">The instance of the <see cref="Dictionary{TKey,TValue}" /> to convert.</param>
    /// <typeparam name="TKey">The expected type of key in <paramref name="instance" />.</typeparam>
    /// <typeparam name="TValue">The expected type of value in <paramref name="instance" />.</typeparam>
    /// <returns>The string representation of the <paramref name="instance" /> suitable for console output.</returns>
    public static string ToConsoleString<TKey, TValue>(this Dictionary<TKey, TValue> instance) =>
        instance?.ToStringValue() ?? "null";
}
