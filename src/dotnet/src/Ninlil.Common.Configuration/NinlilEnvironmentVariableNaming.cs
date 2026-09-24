using System.Text;
using System.Text.RegularExpressions;

// Define our namespace.
namespace Ninlil.Common.Configuration;

/// <summary>
///     This static class turns a real internal ASP.NET Core configuration path (colon-separated
///     section/key segments, each written in ordinary .NET PascalCase -- for example
///     <c>DnsServer:Web:HttpPort</c>) into this project's own single, consistent
///     <c>NINLIL_</c>-prefixed, single-underscore-only environment variable name (for example
///     <c>NINLIL_DNS_SERVER_WEB_HTTP_PORT</c>). Every PascalCase segment is split into its own
///     underscore-joined words and uppercased; the whole name is prefixed with the literal
///     <c>NINLIL_</c> and never contains a double underscore.
///     <para>
///         This is a pure, one-directional transform -- it is never run in reverse. General
///         algorithmic reversal of "which underscore was a hierarchy separator and which was a
///         word split" is genuinely ambiguous once both use the same character, so the actual
///         environment-variable-name-to-configuration-path lookup this project needs at runtime is
///         always driven by an explicit, pre-built mapping table (<see cref="OptionsEnvironmentVariableMap" />),
///         never by trying to invert this method.
///     </para>
/// </summary>
public static class NinlilEnvironmentVariableNaming
{
    /// <summary>
    ///     This constant contains the literal prefix every environment variable name this project
    ///     recognizes must start with.
    /// </summary>
    public const string Prefix = "NINLIL_";

    /// <summary>
    ///     This field lists every irregular PascalCase token this codebase's own configuration
    ///     property names actually contain that ordinary word-boundary detection cannot split
    ///     correctly on its own -- specifically, an IPv4/IPv6 version suffix glued directly onto an
    ///     acronym with no capitalized letter marking a new word boundary (<c>IPv4</c> is really
    ///     "IP" + "v4", not "I" + "Pv4" or "IP" + "V4"). Each entry's replacement is what the final,
    ///     uppercased word becomes. This is a deliberately small, explicit exception list rather
    ///     than a more "clever" general rule, since exactly these two tokens are the only real
    ///     occurrences in this project's own configuration classes as of this writing (see
    ///     <c>EdnsClientSubnetOptions.SourcePrefixLengthIPv4</c>/<c>...IPv6</c> and
    ///     <c>DnsRateLimitOptions.SubnetPrefixLengthIPv4</c>/<c>...IPv6</c>) -- extend this list by
    ///     hand if a future property ever introduces a similarly irregular token.
    /// </summary>
    private static readonly (string Pattern, string Replacement)[] IrregularTokens =
    [
        ("IPv6", "IPV6"),
        ("IPv4", "IPV4"),
    ];

    /// <summary>
    ///     This field is the compiled regular expression that splits a PascalCase segment around
    ///     every irregular token in <see cref="IrregularTokens" />, so each token can be replaced as
    ///     one atomic unit before the ordinary word-boundary splitter in
    ///     <see cref="SplitOrdinaryPascalCaseWords" /> ever sees the surrounding text.
    /// </summary>
    private static readonly Regex IrregularTokenSplitter = new(
        "(" + string.Join("|", IrregularTokens.Select(token => Regex.Escape(token.Pattern))) + ")",
        RegexOptions.Compiled
    );

    /// <summary>
    ///     This method converts a full, colon-separated configuration path into its own
    ///     <c>NINLIL_</c>-prefixed environment variable name.
    /// </summary>
    /// <param name="configurationPath">
    ///     The real internal configuration path (for example <c>DnsServer:Web:HttpPort</c>), each
    ///     segment written in ordinary PascalCase.
    /// </param>
    /// <returns>The equivalent <c>NINLIL_</c>-prefixed environment variable name.</returns>
    public static string ToEnvironmentVariableName(string configurationPath)
    {
        // Every ":"-separated configuration-path segment becomes its own group of underscore-
        // joined words; the whole name is then prefixed and every word is already uppercased by
        // SplitPascalCaseSegmentIntoWords below, so only the final "_" join is needed here.
        IEnumerable<string> segments = configurationPath.Split(
            ':',
            StringSplitOptions.RemoveEmptyEntries
        );
        IEnumerable<string> words = segments.SelectMany(SplitPascalCaseSegmentIntoWords);
        return Prefix + string.Join('_', words);
    }

    /// <summary>
    ///     This method splits one single PascalCase configuration-path segment (never containing a
    ///     <c>:</c> itself) into its own uppercased words, protecting every irregular token in
    ///     <see cref="IrregularTokens" /> from the ordinary word-boundary splitter first.
    /// </summary>
    internal static IEnumerable<string> SplitPascalCaseSegmentIntoWords(string segment)
    {
        // Regex.Split with a capturing group interleaves the text between matches with the
        // matches themselves -- every even-indexed element is ordinary PascalCase text to run
        // through the generic splitter below, and every odd-indexed element is one of the
        // irregular tokens themselves, already known verbatim.
        string[] parts = IrregularTokenSplitter.Split(segment);
        List<string> words = [];

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0)
                continue;

            if (i % 2 == 1)
            {
                // An irregular token match -- look up its own fixed uppercased replacement rather
                // than running it through the generic splitter, which would split it wrong.
                (string Pattern, string Replacement) match = IrregularTokens.First(token =>
                    token.Pattern == parts[i]
                );
                words.Add(match.Replacement);
            }
            else
            {
                words.AddRange(
                    SplitOrdinaryPascalCaseWords(parts[i]).Select(word => word.ToUpperInvariant())
                );
            }
        }

        return words;
    }

    /// <summary>
    ///     This method splits an ordinary PascalCase string (containing none of the irregular
    ///     tokens above) into its own individual words, using the standard rule every other word in
    ///     this project's own configuration property names already follows: a new word starts at
    ///     any uppercase letter immediately preceded by a lowercase letter or digit (<c>HttpPort</c>
    ///     -&gt; <c>Http</c>, <c>Port</c>), and at the last uppercase letter of an acronym run that
    ///     is immediately followed by a lowercase letter (<c>HTTPPort</c> -&gt; <c>HTTP</c>,
    ///     <c>Port</c>) -- this second rule does not fire inside this project's own property names
    ///     today (no acronym run longer than a single capitalized word appears anywhere) but is kept
    ///     for a future property that might introduce one.
    /// </summary>
    private static IEnumerable<string> SplitOrdinaryPascalCaseWords(string text)
    {
        List<string> words = [];
        StringBuilder current = new();

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // A new word starts at an uppercase letter that either follows a lowercase
            // letter/digit directly (the "Http" in "HttpPort"), or is the last letter of an
            // acronym run immediately followed by a lowercase letter (the second "P" in
            // "HTTPPort" starts "Port", not "P"). current.Length > 0 guards the very first
            // character, which can never start a new word since there is nothing before it yet.
            bool startsNewWord =
                current.Length > 0
                && char.IsUpper(c)
                && (
                    char.IsLower(text[i - 1])
                    || char.IsDigit(text[i - 1])
                    || (
                        char.IsUpper(text[i - 1])
                        && i + 1 < text.Length
                        && char.IsLower(text[i + 1])
                    )
                );

            // Flush the word built so far before starting the new one, then append the current
            // character to whichever word (the flushed one or the new one) it actually belongs to.
            if (startsNewWord)
            {
                words.Add(current.ToString());
                current.Clear();
            }

            current.Append(c);
        }

        if (current.Length > 0)
            words.Add(current.ToString());

        return words;
    }
}
