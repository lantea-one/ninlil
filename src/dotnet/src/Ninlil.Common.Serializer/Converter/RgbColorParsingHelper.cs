using System.Drawing;
using System.Text.RegularExpressions;

// Define our namespace.
namespace Ninlil.Common.Serializer.Converter;

/// <summary>
///     This internal static class provides a shared helper for parsing the CSS-style
///     <c>rgb(r, g, b)</c> (or <c>rgba(r, g, b, a)</c>, with the alpha component ignored) functional
///     notation string that <see cref="RgbColorJsonConverter" />, <see cref="NullableRgbColorJsonConverter" />,
///     and <see cref="RgbColorYamlConverter" /> all write, into a <see cref="Color" />.
/// </summary>
/// <remarks>
///     This helper exists because <see cref="ColorTranslator.FromHtml(string)" /> -- despite its name
///     suggesting broad HTML/CSS color support -- does not actually understand the "rgb(r, g, b)"
///     functional notation at all (it supports only hex codes such as "#RRGGBB", known/system color
///     names, and a bare "r,g,b" form with no surrounding "rgb(...)"). Passing the very string these
///     converters write back into <see cref="ColorTranslator.FromHtml(string)" /> throws, so the round
///     trip these converters exist to provide was broken for every non-empty color value until this
///     helper was introduced.
/// </remarks>
internal static class RgbColorParsingHelper
{
    /// <summary>
    ///     This pattern matches an <c>rgb(r, g, b)</c> or <c>rgba(r, g, b, a)</c> string, tolerating
    ///     optional whitespace around each component and case-insensitivity of the "rgb"/"rgba" prefix.
    ///     The alpha component, if present, is matched but not used -- these converters treat color as
    ///     fully opaque.
    /// </summary>
    private static readonly Regex Pattern = new(
        @"^rgba?\(\s*(?<r>\d{1,3})\s*,\s*(?<g>\d{1,3})\s*,\s*(?<b>\d{1,3})\s*(?:,\s*[\d.]+\s*)?\)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>
    ///     This method attempts to parse the provided <paramref name="value" /> string as an
    ///     <c>rgb(r, g, b)</c> (or <c>rgba(r, g, b, a)</c>) functional-notation color string.
    /// </summary>
    /// <param name="value">The string to parse.</param>
    /// <param name="color">
    ///     The output <see cref="Color" /> if parsing succeeds; otherwise, <see cref="Color.Empty" />.
    /// </param>
    /// <returns><see langword="true" /> if parsing succeeded; otherwise, <see langword="false" />.</returns>
    public static bool TryParse(string value, out Color color)
    {
        // Default the output color to empty in case parsing fails below.
        color = Color.Empty;

        // Ensure we have a non-empty string to work with.
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // Match the trimmed input against our rgb(...)/rgba(...) pattern.
        Match match = Pattern.Match(value.Trim());

        // If the pattern didn't match, or any of the three color components fail to parse as a
        // byte (0-255), we cannot produce a color, so return false.
        if (
            !match.Success
            || !byte.TryParse(match.Groups["r"].Value, out byte r)
            || !byte.TryParse(match.Groups["g"].Value, out byte g)
            || !byte.TryParse(match.Groups["b"].Value, out byte b)
        )
            return false;

        // We're done, construct the fully-opaque color from the parsed components and return true.
        color = Color.FromArgb(255, r, g, b);
        return true;
    }
}
