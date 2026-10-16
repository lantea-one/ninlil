// Define our namespace.
namespace Ninlil.Common.Extension.String;

/// <summary>
///     This static class strips quoting and brackets from a string before
///     <see cref="GetTypeValueStringExtension" /> converts it to a typed value.
/// </summary>
public static class SanitizeStringExtension
{
    /// <summary>
    ///     This method removes one pair of surrounding double or single quotes, unescapes the
    ///     escaped form of that quote up to <paramref name="depth" /> levels deep, and trims the
    ///     result. A lone quote character is returned trimmed and otherwise unchanged.
    /// </summary>
    /// <param name="instance">The <see cref="string" /> instance to sanitize.</param>
    /// <param name="depth">The number of backslash-escape levels to unescape.</param>
    /// <returns>The sanitized <see cref="string" /> <paramref name="instance" />.</returns>
    public static string SanitizeForValueTyping(this string instance, int depth = 13)
    {
        // Strip one surrounding quote pair of either kind, never both: an inner pair such as the
        // single quotes of "'x'" belongs to the value. A length of at least 2 keeps a lone quote
        // from counting as both the opening and the closing quote.
        char quote =
            instance is { Length: >= 2 } && instance[0] is '"' or '\'' ? instance[0] : '\0';

        if (quote != '\0' && instance[^1] == quote)
        {
            instance = instance.Substring(1, instance.Length - 2);

            // Each loop iteration collapses one more level of escaping (level+1 backslashes before
            // the quote become level backslashes), so a value quoted and escaped several times
            // comes back to its content. Only the quote that wrapped the value is unescaped.
            for (int level = 0; level < depth; ++level)
                instance = instance.Replace(
                    $"{new string('\\', level + 1)}{quote}",
                    $"{new string('\\', level)}{quote}"
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
