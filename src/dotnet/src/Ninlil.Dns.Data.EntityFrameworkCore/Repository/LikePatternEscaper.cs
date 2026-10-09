// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class turns user-typed text into a <c>LIKE</c> pattern that matches it literally.
///     <see cref="DnsQueryLogRepository" /> uses it for the domain search, so a term such as
///     <c>_dmarc</c> or <c>100%</c> matches those characters instead of acting as a wildcard.
///     The pattern must be passed to <c>EF.Functions.Like</c> or <c>EF.Functions.ILike</c> with
///     <see cref="EscapeCharacter" /> as the escape argument.
/// </summary>
internal static class LikePatternEscaper
{
    /// <summary>
    ///     This constant contains the escape character: a single backslash. It is the escape
    ///     character PostgreSQL and MySQL use by default and is passed explicitly on every
    ///     provider, because SQL Server and SQLite have none by default.
    /// </summary>
    public const string EscapeCharacter = "\\";

    /// <summary>
    ///     This method escapes the escape character itself, the wildcards <c>%</c> and <c>_</c>,
    ///     and <c>[</c>, which opens a character class on SQL Server.
    /// </summary>
    /// <param name="term">The text to match literally.</param>
    /// <returns>The escaped text, without the surrounding <c>%</c> wildcards.</returns>
    public static string Escape(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    /// <summary>
    ///     This method builds the "contains" pattern for <paramref name="term" />: the escaped
    ///     text between two <c>%</c> wildcards.
    /// </summary>
    /// <param name="term">The text to find inside a column value.</param>
    /// <returns>The pattern to pass to <c>Like</c> or <c>ILike</c>.</returns>
    public static string Contains(string term) => $"%{Escape(term)}%";
}
