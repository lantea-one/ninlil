using System.Text.RegularExpressions;

// Define our namespace.
namespace Ninlil.Common.Extension.String;

/// <summary>
///     Extensions for the <see cref="string"/> class to provide the Like functionality.
/// </summary>
public static class LikeStringExtension
{
    /// <summary>
    ///     Check if a string <paramref name="instance" /> matches the <paramref name="searchPattern" />
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
    ///     <paramref name="searchPattern"/>, otherwise <see langword="false"/>.
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
        // If it fails, we assume it's a glob pattern and convert it.
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
    ///     Check if a string <paramref name="instance" /> matches the <paramref name="searchPattern" />
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
