using System.Text;
using System.Text.RegularExpressions;

// Define our namespace.
namespace Ninlil.Common.Configuration;

/// <summary>
///     This static class turns an ASP.NET Core configuration path (colon-separated PascalCase
///     segments, for example <c>DnsServer:Web:HttpPort</c>) into Ninlil's environment variable
///     name for it (for example <c>NINLIL_DNS_SERVER_WEB_HTTP_PORT</c>). Each PascalCase segment is
///     split into words, every word is uppercased, the words are joined with single underscores and
///     the result is prefixed with <c>NINLIL_</c>. The name never contains a double underscore.
///     <para>
///         The transform only runs one way. Once the hierarchy separator and the word separator
///         are the same character, a name cannot be reliably turned back into a path, so the
///         runtime lookup from variable name to configuration path uses the table that
///         <see cref="OptionsEnvironmentVariableMap" /> builds instead.
///     </para>
/// </summary>
public static class NinlilEnvironmentVariableNaming
{
    /// <summary>
    ///     This constant contains the prefix that every recognized environment variable name starts
    ///     with.
    /// </summary>
    public const string Prefix = "NINLIL_";

    /// <summary>
    ///     This field lists the PascalCase tokens that the ordinary word splitter cannot split
    ///     correctly: an IP version suffix glued onto an acronym with no capital letter marking the
    ///     boundary (<c>IPv4</c> is "IP" + "v4", not "I" + "Pv4"). Each replacement is the final,
    ///     uppercased word. Configuration properties such as
    ///     <c>EdnsClientSubnetOptions.SourcePrefixLengthIPv4</c> and
    ///     <c>DnsRateLimitOptions.SubnetPrefixLengthIPv6</c> depend on it. Add an entry here if a
    ///     new property introduces another irregular token.
    /// </summary>
    private static readonly (string Pattern, string Replacement)[] IrregularTokens =
    [
        ("IPv6", "IPV6"),
        ("IPv4", "IPV4"),
    ];

    /// <summary>
    ///     This field is the regular expression that splits a PascalCase segment around every token
    ///     in <see cref="IrregularTokens" />, so each token is replaced as a unit before
    ///     <see cref="SplitOrdinaryPascalCaseWords" /> sees the surrounding text.
    /// </summary>
    private static readonly Regex IrregularTokenSplitter = new(
        "(" + string.Join("|", IrregularTokens.Select(token => Regex.Escape(token.Pattern))) + ")",
        RegexOptions.Compiled
    );

    /// <summary>
    ///     This method converts a colon-separated configuration path into its <c>NINLIL_</c>-prefixed
    ///     environment variable name.
    /// </summary>
    /// <param name="configurationPath">
    ///     The configuration path (for example <c>DnsServer:Web:HttpPort</c>), with each segment in
    ///     PascalCase.
    /// </param>
    /// <returns>The equivalent <c>NINLIL_</c>-prefixed environment variable name.</returns>
    public static string ToEnvironmentVariableName(string configurationPath)
    {
        // SplitPascalCaseSegmentIntoWords already uppercases every word, so only the final "_"
        // join and the prefix are left to do here.
        IEnumerable<string> segments = configurationPath.Split(
            ':',
            StringSplitOptions.RemoveEmptyEntries
        );
        IEnumerable<string> words = segments.SelectMany(SplitPascalCaseSegmentIntoWords);
        return Prefix + string.Join('_', words);
    }

    /// <summary>
    ///     This method splits one PascalCase configuration-path segment (which never contains a
    ///     <c>:</c>) into uppercased words. Tokens in <see cref="IrregularTokens" /> are taken out
    ///     first so the ordinary word splitter never sees them.
    /// </summary>
    internal static IEnumerable<string> SplitPascalCaseSegmentIntoWords(string segment)
    {
        // Regex.Split with a capturing group interleaves the text between matches with the
        // matches themselves: even indexes hold ordinary PascalCase text, odd indexes hold an
        // irregular token.
        string[] parts = IrregularTokenSplitter.Split(segment);
        List<string> words = [];

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0)
                continue;

            if (i % 2 == 1)
            {
                // An irregular token: use its fixed replacement, since the generic splitter would
                // split it wrong.
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
    ///     This method splits an ordinary PascalCase string (one with no irregular tokens) into
    ///     words. A new word starts at an uppercase letter that follows a lowercase letter or digit
    ///     (<c>HttpPort</c> -&gt; <c>Http</c>, <c>Port</c>), and at the last uppercase letter of an
    ///     acronym run that is followed by a lowercase letter (<c>HTTPPort</c> -&gt; <c>HTTP</c>,
    ///     <c>Port</c>).
    /// </summary>
    private static IEnumerable<string> SplitOrdinaryPascalCaseWords(string text)
    {
        List<string> words = [];
        StringBuilder current = new();

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // current.Length > 0 guards the first character, which cannot start a new word
            // because nothing comes before it.
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
