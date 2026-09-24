using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

// Define our namespace.
namespace Ninlil.Common.Extension.String;

/// <summary>
///     This class contains an extension method for the <see cref="string" /> class to
///     determine if a <see cref="string" /> is a valid type of data or not.
/// </summary>
public static class IsStringExtension
{
    /// <summary>
    ///     This method is used to check if the <see cref="string" />
    ///     <paramref name="instance" /> is a valid <see cref="bool" /> value or not.
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
    ///     This method is used to check if the <see cref="string" />
    ///     <paramref name="instance"/> is a valid <see cref="DateTime" /> value or not.
    /// </summary>
    /// <param name="instance"> The <see cref="string" /> instance to check.</param>
    /// <returns>
    ///     A <see cref="bool" /> value indicating whether the <see cref="string" />
    ///     is a valid <see cref="DateTime" /> value or not.
    /// </returns>
    public static bool IsDate(this string instance) =>
        instance?.Trim() is not (null or "") && DateTime.TryParse(instance.Trim(), out _);

    /// <summary>
    ///     This method is used to check if the <see cref="string" />
    ///     <paramref name="instance"/> is a valid <see cref="DateTime" /> value or not.
    /// </summary>
    /// <param name="instance"> The <see cref="string" /> instance to check.</param>
    /// <returns>
    ///     A <see cref="bool" /> value indicating whether the <see cref="string" />
    ///     is a valid <see cref="DateTime" /> value or not.
    /// </returns>
    public static bool IsDateTime(this string instance) => IsDate(instance);

    /// <summary>
    ///     This method is used to check if the <see cref="string" />
    ///     <paramref name="instance"/> is a valid <see cref="double" /> value or not.
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
    ///     This method is used to check if the <see cref="string" />
    ///     <paramref name="instance" /> is a valid email address value or not.
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
    ///     This method is used to check if the <see cref="string" />
    ///     <paramref name="instance"/> is a valid <see cref="Guid" /> value or not.
    /// </summary>
    /// <param name="instance"> The <see cref="string" /> instance to check.</param>
    /// <returns>
    ///     A <see cref="bool" /> value indicating whether the <see cref="string" />
    ///     is a valid <see cref="Guid" /> value or not.
    /// </returns>
    public static bool IsGuid(this string instance) =>
        instance?.Trim() is not (null or "") && Guid.TryParse(instance.Trim(), out _);

    /// <summary>
    ///     This method is used to check if the <see cref="string" />
    ///     <paramref name="instance" /> is a valid <see cref="int" /> value or not.
    /// </summary>
    /// <param name="instance"> The <see cref="string" /> instance to check.</param>
    /// <returns>
    ///     A <see cref="bool" /> value indicating whether the <see cref="string" />
    ///     is a valid <see cref="int" /> value or not.
    /// </returns>
    public static bool IsInt(this string instance) =>
        instance?.Trim() is not (null or "") && int.TryParse(instance.Trim(), out _);

    /// <summary>
    ///     This method is used to check if the <see cref="string" />
    ///     <paramref name="instance" /> is a valid money or <see cref="double" /> value or not.
    /// </summary>
    /// <param name="instance"> The <see cref="string" /> instance to check.</param>
    /// <returns>
    ///     A <see cref="bool" /> value indicating whether the <see cref="string" />
    ///     is a valid money or <see cref="double" /> value or not.
    /// </returns>
    public static bool IsMoney(this string instance) => IsDouble(instance);
}
