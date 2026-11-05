// Define our namespace.
namespace Ninlil.Common.Extension.Enum;

/// <summary>
///     This static class parses text from an API input into one member of an enum, by member name
///     only. <see cref="System.Enum.TryParse{TEnum}(string, bool, out TEnum)" /> also takes digit
///     strings (including numbers that are no member) and, for a <see cref="FlagsAttribute" />
///     enum, comma lists and masks; an API input must refuse all of those so that the stored text
///     is always a name a reader can look up. Controllers call
///     <see cref="TryParseName{TEnum}(string, bool, out TEnum, out string)" /> and answer 400 with
///     <see cref="GetNames{TEnum}" /> when it returns <see langword="false" />.
/// </summary>
public static class EnumNameParser
{
    /// <summary>
    ///     Parses <paramref name="text" /> as the exact name of one defined member of
    ///     <typeparamref name="TEnum" />. The text is not trimmed, so surrounding whitespace, digit
    ///     strings, comma lists and unknown names are all refused.
    /// </summary>
    /// <param name="text">The text to parse. Null is refused.</param>
    /// <param name="ignoreCase">Whether the name may differ from the member's name in case.</param>
    /// <param name="value">The member, or the default value when the text is refused.</param>
    /// <param name="canonicalName">The member's declared name, or null when the text is refused.</param>
    /// <typeparam name="TEnum">The enum type, flags or not.</typeparam>
    /// <returns><see langword="true" /> when the text names a member.</returns>
    public static bool TryParseName<TEnum>(
        string text,
        bool ignoreCase,
        out TEnum value,
        out string canonicalName
    )
        where TEnum : struct, System.Enum
    {
        value = default;
        canonicalName = null;

        if (text is null)
            return false;

        StringComparison comparison = ignoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (string name in System.Enum.GetNames<TEnum>())
        {
            if (!string.Equals(name, text, comparison))
                continue;

            value = System.Enum.Parse<TEnum>(name);
            canonicalName = name;
            return true;
        }

        return false;
    }

    /// <summary>
    ///     Parses <paramref name="text" /> as the exact name of one defined member; see
    ///     <see cref="TryParseName{TEnum}(string, bool, out TEnum, out string)" />.
    /// </summary>
    /// <param name="text">The text to parse. Null is refused.</param>
    /// <param name="ignoreCase">Whether the name may differ from the member's name in case.</param>
    /// <param name="value">The member, or the default value when the text is refused.</param>
    /// <typeparam name="TEnum">The enum type, flags or not.</typeparam>
    /// <returns><see langword="true" /> when the text names a member.</returns>
    public static bool TryParseName<TEnum>(string text, bool ignoreCase, out TEnum value)
        where TEnum : struct, System.Enum => TryParseName(text, ignoreCase, out value, out _);

    /// <summary>
    ///     Returns the member names of <typeparamref name="TEnum" /> joined with commas, for the
    ///     "valid values" part of a 400 message.
    /// </summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    public static string GetNames<TEnum>()
        where TEnum : struct, System.Enum => string.Join(", ", System.Enum.GetNames<TEnum>());
}
