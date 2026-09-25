// Define our namespace.
namespace Ninlil.Common.Extension.String;

/// <summary>
///     This static class strips quoting and brackets from a string before
///     <see cref="GetTypeValueStringExtension" /> converts it to a typed value.
/// </summary>
public static class SanitizeStringExtension
{
    /// <summary>
    ///     This method removes one pair of surrounding double or single quotes, unescapes escaped
    ///     double quotes up to <paramref name="depth" /> levels deep, and trims the result.
    /// </summary>
    /// <param name="instance">The <see cref="string" /> instance to sanitize.</param>
    /// <param name="depth">The number of backslash-escape levels to unescape.</param>
    /// <returns>The sanitized <see cref="string" /> <paramref name="instance" />.</returns>
    public static string SanitizeForValueTyping(this string instance, int depth = 13)
    {
        // Remove the surrounding quote pair once, outside the loop. Each loop iteration then
        // collapses one more level of escaping (level+1 backslashes before a quote become level
        // backslashes), so a value quoted and escaped several times comes back to its content.
        // Stripping inside the loop would cut two characters per iteration and throw on short
        // values.
        if (instance?.StartsWith('"') is true && instance.EndsWith('"'))
        {
            instance = instance.Substring(1, instance.Length - 2);

            for (int level = 0; level < depth; ++level)
                instance = instance.Replace(
                    $@"{string.Join("", System.Linq.Enumerable.Repeat('\\', level + 1))}""",
                    $@"{string.Join("", System.Linq.Enumerable.Repeat('\\', level))}"""
                );
        }

        // Do the same for a single-quoted string. The loop unescapes double quotes, as above.
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
    ///     This method strips the surrounding braces of a dictionary literal (<c>{a:1,b:2}</c>) and
    ///     trims the result.
    /// </summary>
    /// <param name="instance">The <see cref="string" /> instance to sanitize.</param>
    /// <returns>The sanitized <see cref="string" /> <paramref name="instance" />.</returns>
    public static string SanitizeForValueTypingDictionary(this string instance) =>
        instance?.TrimEnd('}').TrimStart('{').Trim();

    /// <summary>
    ///     This method strips the surrounding parentheses of a tuple literal (<c>(a,1)</c>) and trims
    ///     the result.
    /// </summary>
    /// <param name="instance">The <see cref="string" /> instance to sanitize.</param>
    /// <returns>The sanitized <see cref="string" /> <paramref name="instance" />.</returns>
    public static string SanitizeForValueTypingTuple(this string instance) =>
        instance?.TrimEnd(')').TrimStart('(').Trim();
}
