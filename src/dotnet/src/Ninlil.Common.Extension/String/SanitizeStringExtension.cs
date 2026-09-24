// Define our namespace.
namespace Ninlil.Common.Extension.String;

/// <summary>
///     This class provides extensions for sanitizing strings.
/// </summary>
public static class SanitizeStringExtension
{
    /// <summary>
    ///     This method is used to sanitize a string for use when determining the typed value of a <see cref="string" />.
    /// </summary>
    /// <param name="instance">The <see cref="string" /> instance to sanitize.</param>
    /// <param name="depth">The depth to which to unescape nested quotes.</param>
    /// <returns>The sanitized <see cref="string" /> <paramref name="instance" />.</returns>
    public static string SanitizeForValueTyping(this string instance, int depth = 13)
    {
        // Check for a double-quoted string and remove the surrounding quote pair exactly once,
        // outside the loop. The loop below then progressively unescapes one additional level of
        // backslash-escaped quotes per iteration (level+1 backslashes collapse to level
        // backslashes), so a value that was quoted and escaped several times over comes back down
        // to its original content. An earlier version of this method re-ran the outer
        // Substring(1, Length - 2) call on every iteration instead of once, unconditionally
        // re-stripping two more characters off the string up to `depth` times regardless of
        // whether there was still anything left to strip -- which threw
        // ArgumentOutOfRangeException for any realistic short quoted value long before `depth`
        // iterations could complete.
        if (instance?.StartsWith('"') is true && instance.EndsWith('"'))
        {
            instance = instance.Substring(1, instance.Length - 2);

            for (int level = 0; level < depth; ++level)
                instance = instance.Replace(
                    $@"{string.Join("", System.Linq.Enumerable.Repeat('\\', level + 1))}""",
                    $@"{string.Join("", System.Linq.Enumerable.Repeat('\\', level))}"""
                );
        }

        // Check for a single-quoted string and remove the surrounding quote pair exactly once;
        // same progressive unescaping as the double-quoted case above.
        if (instance?.StartsWith('\'') is true && instance.EndsWith('\''))
        {
            instance = instance.Substring(1, instance.Length - 2);

            for (int level = 0; level < depth; ++level)
                instance = instance.Replace(
                    $@"{string.Join("", System.Linq.Enumerable.Repeat('\\', level + 1))}""",
                    $@"{string.Join("", System.Linq.Enumerable.Repeat('\\', level))}"""
                );
        }

        // We're done, return the trimmed instance.
        return instance?.Trim();
    }

    /// <summary>
    ///     This method is used to sanitize a string for use when determining
    ///     the typed value of a <see cref="string" /> in a dictionary.
    /// </summary>
    /// <param name="instance">The <see cref="string" /> instance to sanitize.</param>
    /// <returns>The sanitized <see cref="string" /> <paramref name="instance" />.</returns>
    public static string SanitizeForValueTypingDictionary(this string instance) =>
        instance?.TrimEnd('}').TrimStart('{').Trim();

    /// <summary>
    ///     This method is used to sanitize a string for use when determining
    ///     the typed value of a <see cref="string" /> in a tuple.
    /// </summary>
    /// <param name="instance">The <see cref="string" /> instance to sanitize.</param>
    /// <returns>The sanitized <see cref="string" /> <paramref name="instance" />.</returns>
    public static string SanitizeForValueTypingTuple(this string instance) =>
        instance?.TrimEnd(')').TrimStart('(').Trim();
}
