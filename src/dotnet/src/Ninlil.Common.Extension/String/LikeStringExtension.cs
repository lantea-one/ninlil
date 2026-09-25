using System.Text.RegularExpressions;

// Define our namespace.
namespace Ninlil.Common.Extension.String;

/// <summary>
///     This static class adds a <c>Like</c> match to <see cref="string" /> that accepts either a
///     regular expression or a glob (<c>*</c> and <c>?</c>).
/// </summary>
public static class LikeStringExtension
{
    /// <summary>
    ///     This method checks whether the trimmed <paramref name="instance" /> matches
    ///     <paramref name="searchPattern" />. The pattern is tried as a regular expression first; only
    ///     if it is not a valid regular expression is it treated as a whole-string glob, matched
    ///     ignoring case. A pattern that is valid as both (such as <c>a?b</c>) is used as a regular
    ///     expression.
    /// </summary>
    /// <param name="instance">
    ///     The instance of the <see cref="string" /> to compare to the <paramref name="searchPattern" />.
    /// </param>
    /// <param name="searchPattern">
    ///     The glob or <see cref="Regex" /> pattern to compare the <see cref="string"/> <paramref name="instance"/> to.
    /// </param>
    /// <param name="regexOptions">
    ///     The <see cref="RegexOptions"/> to use when matching the
    ///     <paramref name="instance"/> to the <paramref name="searchPattern"/>.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> if the <paramref name="instance"/> matches the
    ///     <paramref name="searchPattern"/>, otherwise <see langword="false"/> (always for a blank
    ///     <paramref name="instance" />).
    /// </returns>
    public static bool Like(this string instance, string searchPattern, RegexOptions regexOptions)
    {
        // Check for a null or empty instance then return false to avoid unnecessary processing.
        if (instance?.Trim() is null or "")
            return false;

        // Try for a direct regular expression match first.
        try
        {
            // Return the direct match result.
            return Regex.IsMatch(instance.Trim(), searchPattern, regexOptions);
        }
        // An invalid regular expression is treated as a glob pattern.
        catch (ArgumentException)
        {
            // Convert the glob pattern to a regular expression.
            return Regex.IsMatch(
                instance.Trim(),
                $"^{Regex.Escape(searchPattern).Replace(@"\*", ".*").Replace(@"\?", ".")}$",
                regexOptions | RegexOptions.IgnoreCase
            );
        }
    }

    /// <summary>
    ///     This method checks whether <paramref name="instance" /> matches
    ///     <paramref name="searchPattern" />, ignoring case. See
    ///     <see cref="Like(string, string, RegexOptions)" /> for how the pattern is read.
    /// </summary>
    /// <param name="instance">
    ///     The instance of the <see cref="string" /> to compare to the <paramref name="searchPattern" />.
    /// </param>
    /// <param name="searchPattern">
    ///     The glob or <see cref="Regex" /> pattern to compare the <see cref="string"/> <paramref name="instance"/> to.
    /// </param>
    /// <returns>
    ///     <see langword="true"/> if the <paramref name="instance"/> matches the
    ///     <paramref name="searchPattern"/>, otherwise <see langword="false"/>.
    /// </returns>
    public static bool Like(this string instance, string searchPattern) =>
        Like(instance, searchPattern, RegexOptions.IgnoreCase);
}
