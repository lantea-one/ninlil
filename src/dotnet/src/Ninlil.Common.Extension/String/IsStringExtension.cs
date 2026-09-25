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
    ///     This method checks whether the <paramref name="instance" /> is a boolean word: true/false,
    ///     yes/no, t/f, y/n or 1/0, ignoring case.
    /// </summary>
    /// <param name="instance"> The <see cref="string" /> instance to check.</param>
    /// <returns>
    ///     A <see cref="bool" /> value indicating whether the <see cref="string" />
    ///     is a valid <see cref="bool" /> value or not.
    /// </returns>
    public static bool IsBoolean(this string instance) =>
        instance?.Trim() is not (null or "")
        && Regex.IsMatch(
            instance.Trim(),
            "^(true|false|yes|no|t|f|y|n|1|0)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

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
