using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

// Define our namespace.
namespace Ninlil.Common.Extension.String;

/// <summary>
///     This static class checks whether a string can be read as a given kind of value (boolean,
///     date, number, email address, GUID). Every check returns <see langword="false" /> for a
///     blank string.
/// </summary>
public static class IsStringExtension
{
    /// <summary>
    ///     The words that read as <see langword="true" />. <see cref="IsBoolean(string)" /> and
    ///     <see cref="GetTypeValueStringExtension" /> share this list and
    ///     <see cref="FalseWords" /> so the check and the conversion cannot drift apart.
    /// </summary>
    private static readonly string[] TrueWords = ["true", "yes", "on", "t", "y", "1"];

    /// <summary>
    ///     The words that read as <see langword="false" />; see <see cref="TrueWords" />.
    /// </summary>
    private static readonly string[] FalseWords = ["false", "no", "off", "f", "n", "0"];

    /// <summary>
    ///     This method reads the whole trimmed <paramref name="instance" /> as a boolean word,
    ///     ignoring case. Text that only contains a word (such as "not") is no boolean.
    /// </summary>
    /// <param name="instance">The <see cref="string" /> to read.</param>
    /// <param name="value">The boolean the word stands for, or <see langword="false" /> when there is none.</param>
    /// <returns><see langword="true" /> when the whole text is a boolean word.</returns>
    internal static bool TryReadBoolean(string instance, out bool value)
    {
        string word = instance?.Trim();
        value = false;

        // Whole-string, ordinal, case-insensitive: a substring or regex search would accept any
        // text that merely contains "t", "y", "1" or "on".
        if (TrueWords.Contains(word, StringComparer.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        return FalseWords.Contains(word, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     This method checks whether the <paramref name="instance" /> is a boolean word: true/false,
    ///     yes/no, on/off, t/f, y/n or 1/0, ignoring case.
    /// </summary>
    /// <param name="instance"> The <see cref="string" /> instance to check.</param>
    /// <returns>
    ///     A <see cref="bool" /> value indicating whether the <see cref="string" />
    ///     is a valid <see cref="bool" /> value or not.
    /// </returns>
    public static bool IsBoolean(this string instance) => TryReadBoolean(instance, out _);

    /// <summary>
    ///     This method checks whether the <paramref name="instance" /> parses as a
    ///     <see cref="DateTime" /> in the current culture.
    /// </summary>
    /// <param name="instance"> The <see cref="string" /> instance to check.</param>
    /// <returns>
    ///     A <see cref="bool" /> value indicating whether the <see cref="string" />
    ///     is a valid <see cref="DateTime" /> value or not.
    /// </returns>
    public static bool IsDate(this string instance) =>
        instance?.Trim() is not (null or "") && DateTime.TryParse(instance.Trim(), out _);

    /// <summary>
    ///     This method is an alias of <see cref="IsDate" />.
    /// </summary>
    /// <param name="instance"> The <see cref="string" /> instance to check.</param>
    /// <returns>
    ///     A <see cref="bool" /> value indicating whether the <see cref="string" />
    ///     is a valid <see cref="DateTime" /> value or not.
    /// </returns>
    public static bool IsDateTime(this string instance) => IsDate(instance);

    /// <summary>
    ///     This method checks whether the <paramref name="instance" /> parses as a
    ///     <see cref="double" /> once <c>$</c> and <c>,</c> characters are removed.
    /// </summary>
    /// <param name="instance"> The <see cref="string" /> instance to check.</param>
    /// <returns>
    ///     A <see cref="bool" /> value indicating whether the <see cref="string" />
    ///     is a valid <see cref="double" /> value or not.
    /// </returns>
    public static bool IsDouble(this string instance) =>
        instance?.Trim() is not (null or "")
        && double.TryParse(instance.Replace("$", "").Replace(",", "").Trim(), out _);

    /// <summary>
    ///     This method checks whether the <paramref name="instance" /> passes
    ///     <see cref="EmailAddressAttribute" /> validation.
    /// </summary>
    /// <param name="instance"> The <see cref="string" /> instance to check.</param>
    /// <returns>
    ///     A <see cref="bool" /> value indicating whether the <see cref="string" />
    ///     is a valid email address value or not.
    /// </returns>
    public static bool IsEmailAddress(this string instance) =>
        instance?.Trim() is not (null or "")
        && new EmailAddressAttribute().IsValid(instance.Trim());

    /// <summary>
    ///     This method checks whether the <paramref name="instance" /> parses as a
    ///     <see cref="Guid" />.
    /// </summary>
    /// <param name="instance"> The <see cref="string" /> instance to check.</param>
    /// <returns>
    ///     A <see cref="bool" /> value indicating whether the <see cref="string" />
    ///     is a valid <see cref="Guid" /> value or not.
    /// </returns>
    public static bool IsGuid(this string instance) =>
        instance?.Trim() is not (null or "") && Guid.TryParse(instance.Trim(), out _);

    /// <summary>
    ///     This method checks whether the <paramref name="instance" /> parses as an
    ///     <see cref="int" />.
    /// </summary>
    /// <param name="instance"> The <see cref="string" /> instance to check.</param>
    /// <returns>
    ///     A <see cref="bool" /> value indicating whether the <see cref="string" />
    ///     is a valid <see cref="int" /> value or not.
    /// </returns>
    public static bool IsInt(this string instance) =>
        instance?.Trim() is not (null or "") && int.TryParse(instance.Trim(), out _);

    /// <summary>
    ///     This method is an alias of <see cref="IsDouble" />, so currency amounts such as
    ///     <c>$1,000.50</c> are accepted.
    /// </summary>
    /// <param name="instance"> The <see cref="string" /> instance to check.</param>
    /// <returns>
    ///     A <see cref="bool" /> value indicating whether the <see cref="string" />
    ///     is a valid money or <see cref="double" /> value or not.
    /// </returns>
    public static bool IsMoney(this string instance) => IsDouble(instance);
}
