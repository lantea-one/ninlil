using System.Globalization;
using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering;

/// <summary>
///     This record is the outcome of parsing one subscribed list's downloaded text.
/// </summary>
/// <param name="Entries">Every distinct entry the text produced, in first-seen order.</param>
/// <param name="SkippedRuleCount">
///     How many rules were recognized as rules but could not be expressed as a DNS block --
///     cosmetic (<c>##</c>) rules, URL/path rules, rules with unsupported <c>$</c> modifiers,
///     wildcard or regex rules, and malformed names. Comments, blank lines and the usual
///     hosts-file boilerplate (<c>localhost</c> and friends) are not counted.
/// </param>
public sealed record DnsFilterListParseResult(
    IReadOnlyList<DnsFilterListReplacementEntry> Entries,
    int SkippedRuleCount
)
{
    /// <summary>
    ///     This property contains how many of <see cref="Entries" /> are exceptions.
    /// </summary>
    public int ExceptionCount => Entries.Count(entry => entry.IsException);
}

/// <summary>
///     This class turns the text of a subscribed block/allow list into entries.
///     <see cref="DnsFilterListExternalRefreshService" /> calls it after each download. Every
///     domain entry it produces is a plain <see cref="DnsFilterEntryKindEnum.Domain" /> entry;
///     wildcard and regex rules are skipped.
///     <para>
///         A domain list may be written in any mix of three formats, line by line: hosts-file
///         lines (<c>0.0.0.0 ads.example</c>, <c>127.0.0.1 ads.example</c>,
///         <c>:: ads.example</c>, several names per line allowed), bare domains (one per line),
///         and the DNS-relevant subset of adblock syntax that popular lists such as OISD's ABP
///         variant are published in:
///     </para>
///     <list type="bullet">
///         <item><c>||ads.example^</c> covers <c>ads.example</c> and every subdomain, which is what
///         an ordinary <see cref="DnsFilterEntryKindEnum.Domain" /> entry means, so it is stored as
///         one.</item>
///         <item><c>@@||cdn.ads.example^</c> is an exception, stored with
///         <see cref="DnsFilterListReplacementEntry.IsException" /> set (see
///         <c>DnsFilterListEntryEntity.IsException</c> for why a flag and not a companion list).
///         In a list subscribed with the Allow action there is nothing for an exception to carve
///         out of, and such lists (allowlists published in adblock syntax) are written entirely in
///         <c>@@</c> lines, so there an exception is stored as an ordinary allow entry.</item>
///         <item><c>!</c> lines and <c>[Adblock Plus 2.0]</c>-style headers are comments, like
///         <c>#</c> lines in a hosts file.</item>
///     </list>
///     <para>
///         Anything else adblock syntax can say has no DNS meaning Ninlil can honour and is
///         skipped (and counted): cosmetic rules (<c>##</c>, <c>#@#</c>, ...), URL and path rules
///         (<c>|http://</c>, <c>/ads/</c>, regexes), rules without the <c>^</c> separator (which
///         mean "any name starting with", not "this domain"), wildcard names, and any <c>$</c>
///         modifier except the few that just mean "the whole site" (<c>$all</c>,
///         <c>$document</c>, <c>$doc</c>). In particular <c>$important</c>, <c>$client=</c>,
///         <c>$dnstype=</c>, <c>$dnsrewrite=</c>, <c>$denyallow=</c> and <c>$badfilter</c> change
///         what a rule means in ways a plain domain entry cannot carry, so dropping the rule is
///         safer than applying it more broadly than its author intended.
///     </para>
///     <para>
///         A network (response-IP or client-source) list is one address or CIDR block per line,
///         taking the first token so that trailing annotations such as Spamhaus DROP's
///         <c>; SBL123</c> are ignored rather than mistaken for the value.
///     </para>
/// </summary>
public static class DnsFilterListParser
{
    /// <summary>
    ///     This field contains the <c>$</c> modifiers that only restate "block the whole site",
    ///     which a DNS block already does.
    /// </summary>
    private static readonly HashSet<string> WholeSiteModifiers = new(
        ["all", "document", "doc"],
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>
    ///     This field contains the names every stock hosts file maps to the loopback or broadcast
    ///     address for local use. They are boilerplate, not blocking rules, and
    ///     blocking <c>localhost</c> would break local software, so they are dropped silently.
    /// </summary>
    private static readonly HashSet<string> HostsFileBoilerplateNames = new(
        [
            "localhost",
            "localhost.localdomain",
            "local",
            "broadcasthost",
            "ip6-localhost",
            "ip6-loopback",
            "ip6-localnet",
            "ip6-mcastprefix",
            "ip6-allnodes",
            "ip6-allrouters",
            "ip6-allhosts",
        ],
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>
    ///     This field converts internationalized names to their ASCII (punycode) form, which is
    ///     what a query carries on the wire. STD3 rules are off so underscores, common in tracker
    ///     hostnames, are accepted.
    /// </summary>
    private static readonly IdnMapping Idn = new() { UseStd3AsciiRules = false };

    /// <summary>
    ///     This method parses a subscribed list's text according to the list's kind and action.
    /// </summary>
    /// <param name="content">The downloaded text.</param>
    /// <param name="kind">The list's kind: domain lists get the full syntax, network lists one address per line.</param>
    /// <param name="action">The list's action: in an allow list, <c>@@</c> exceptions become ordinary entries.</param>
    public static DnsFilterListParseResult Parse(
        string content,
        DnsFilterListKindEnum kind,
        DnsFilterListActionEnum action
    ) =>
        kind == DnsFilterListKindEnum.Domain
            ? ParseDomainList(content, action == DnsFilterListActionEnum.Allow)
            : ParseNetworkList(content);

    /// <summary>
    ///     This method parses a domain list written in hosts, bare-domain or adblock syntax, or any
    ///     mix of them (see the class summary for what is accepted).
    /// </summary>
    /// <param name="content">The downloaded text.</param>
    /// <param name="treatExceptionsAsRules">
    ///     Whether an <c>@@</c> line becomes an ordinary entry rather than an exception; true for a
    ///     list subscribed with the Allow action.
    /// </param>
    public static DnsFilterListParseResult ParseDomainList(
        string content,
        bool treatExceptionsAsRules = false
    )
    {
        List<DnsFilterListReplacementEntry> entries = [];
        HashSet<DnsFilterListReplacementEntry> seen = [];
        int skipped = 0;

        void Add(string domain, bool isException)
        {
            DnsFilterListReplacementEntry entry = new(
                domain,
                isException && !treatExceptionsAsRules
            );

            if (seen.Add(entry))
                entries.Add(entry);
        }

        foreach (string line in SplitLines(content))
        {
            if (IsCommentOrHeader(line))
                continue;

            string rule = StripInlineComment(line);

            if (rule.Length == 0)
                continue;

            // Any '#' still left is part of the rule, which in adblock syntax means an
            // element-hiding (cosmetic) rule: page styling, nothing DNS can do.
            if (rule.Contains('#'))
            {
                skipped++;
                continue;
            }

            if (rule.StartsWith("@@", StringComparison.Ordinal))
            {
                if (TryParseAdblockDomainRule(rule[2..], out string exceptionDomain))
                    Add(exceptionDomain, isException: true);
                else
                    skipped++;

                continue;
            }

            if (rule.StartsWith('|') || rule.StartsWith('/') || rule.Contains('^'))
            {
                if (TryParseAdblockDomainRule(rule, out string blockDomain))
                    Add(blockDomain, isException: false);
                else
                    skipped++;

                continue;
            }

            // Not adblock syntax, so either a hosts-file line or a bare domain.
            string[] tokens = rule.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

            if (tokens.Length == 1)
            {
                // A lone address ("0.0.0.0") is hosts-file residue, not a domain.
                if (LooksLikeAddress(tokens[0]))
                    skipped++;
                else if (IsHostsFileBoilerplate(tokens[0]))
                    continue;
                else if (TryNormalizeDomain(tokens[0], out string bareDomain))
                    Add(bareDomain, isException: false);
                else
                    skipped++;

                continue;
            }

            // A hosts-file line is "<address> <name> [<name> ...]"; anything with several tokens
            // that does not start with an address is not a format this parser knows.
            // (A stock "fe80::1%lo0 localhost" line lands here too, since not every runtime
            // parses a named IPv6 zone; it only names boilerplate, so it is not counted.)
            if (!LooksLikeAddress(tokens[0]))
            {
                if (!tokens.Skip(1).All(IsHostsFileBoilerplate))
                    skipped++;

                continue;
            }

            foreach (string name in tokens.Skip(1))
            {
                if (IsHostsFileBoilerplate(name) || LooksLikeAddress(name))
                    continue;

                if (TryNormalizeDomain(name, out string hostsDomain))
                    Add(hostsDomain, isException: false);
                else
                    skipped++;
            }
        }

        return new DnsFilterListParseResult(entries, skipped);
    }

    /// <summary>
    ///     This method parses a network list: one IP address or CIDR block per line, the first
    ///     whitespace-separated token of each, with <c>#</c>, <c>;</c> and <c>!</c> comments.
    /// </summary>
    /// <param name="content">The downloaded text.</param>
    public static DnsFilterListParseResult ParseNetworkList(string content)
    {
        List<DnsFilterListReplacementEntry> entries = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        int skipped = 0;

        foreach (string line in SplitLines(content))
        {
            if (line.StartsWith('#') || line.StartsWith(';') || line.StartsWith('!'))
                continue;

            string token = line.Split(
                [' ', '\t', ';', '#'],
                2,
                StringSplitOptions.RemoveEmptyEntries
            )[0];

            bool valid = token.Contains('/')
                ? IPNetwork.TryParse(token, out _)
                : IPAddress.TryParse(token, out _) && LooksLikeAddress(token);

            if (!valid)
            {
                skipped++;
                continue;
            }

            string value = token.ToLowerInvariant();

            if (seen.Add(value))
                entries.Add(new DnsFilterListReplacementEntry(value));
        }

        return new DnsFilterListParseResult(entries, skipped);
    }

    /// <summary>
    ///     This method splits text into trimmed, non-empty lines, whatever its line endings, and
    ///     drops a leading byte-order mark.
    /// </summary>
    private static IEnumerable<string> SplitLines(string content) =>
        (content ?? string.Empty)
            .TrimStart('﻿')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);

    /// <summary>
    ///     This method recognizes a whole-line comment in either syntax (<c>#</c> for hosts,
    ///     <c>!</c> for adblock) and an adblock header such as <c>[Adblock Plus 2.0]</c>.
    /// </summary>
    private static bool IsCommentOrHeader(string line) =>
        line.StartsWith('#')
        || line.StartsWith('!')
        || (line.StartsWith('[') && line.EndsWith(']'));

    /// <summary>
    ///     This method removes a trailing hosts-file comment (<c>0.0.0.0 ads.example # why</c>).
    ///     Only a <c>#</c> preceded by whitespace starts a comment; one glued to the rule is an
    ///     adblock cosmetic marker, which the caller rejects.
    /// </summary>
    private static string StripInlineComment(string line)
    {
        for (int index = 1; index < line.Length; index++)
        {
            if (line[index] == '#' && char.IsWhiteSpace(line[index - 1]))
                return line[..index].TrimEnd();
        }

        return line;
    }

    /// <summary>
    ///     This method parses the part of an adblock rule after any <c>@@</c>: it must be
    ///     <c>||name^</c> (optionally <c>^|</c>), optionally followed by whole-site modifiers
    ///     only, naming a plain domain.
    /// </summary>
    private static bool TryParseAdblockDomainRule(string rule, out string domain)
    {
        domain = null;

        if (!rule.StartsWith("||", StringComparison.Ordinal))
            return false;

        string body = rule[2..];
        int modifierStart = body.IndexOf('$');

        if (modifierStart >= 0)
        {
            string[] modifiers = body[(modifierStart + 1)..].Split(',');

            if (!modifiers.All(modifier => WholeSiteModifiers.Contains(modifier.Trim())))
                return false;

            body = body[..modifierStart];
        }

        // Without the '^' separator a rule is a prefix match ("||ads" also matches
        // "adserver.example"), which a domain entry cannot express.
        if (body.EndsWith("^|", StringComparison.Ordinal))
            body = body[..^2];
        else if (body.EndsWith('^'))
            body = body[..^1];
        else
            return false;

        return TryNormalizeDomain(body, out domain);
    }

    /// <summary>
    ///     This method lowercases a name, drops a trailing dot, converts an internationalized
    ///     name to punycode, and checks the result is an ordinary hostname: labels of letters,
    ///     digits, hyphens and underscores, each 1 to 63 characters, 253 characters in total.
    ///     Wildcards, paths, ports and anything else are rejected.
    /// </summary>
    private static bool TryNormalizeDomain(string raw, out string domain)
    {
        domain = null;
        string candidate = raw.TrimEnd('.');

        if (candidate.Length == 0)
            return false;

        if (candidate.Any(character => character > 0x7F))
        {
            try
            {
                candidate = Idn.GetAscii(candidate);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        candidate = candidate.ToLowerInvariant();

        if (candidate.Length > 253)
            return false;

        foreach (string label in candidate.Split('.'))
        {
            if (label.Length is 0 or > 63)
                return false;

            if (
                !label.All(character =>
                    character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_'
                )
            )
                return false;
        }

        domain = candidate;
        return true;
    }

    /// <summary>
    ///     This method checks whether a token is an IP address as written in a hosts file. A
    ///     plain <see cref="IPAddress.TryParse(string, out IPAddress)" /> alone would also accept
    ///     shorthand like <c>"1"</c> or <c>"10.1"</c>, which could be names, so a dotted IPv4 form
    ///     must have all four parts.
    /// </summary>
    private static bool LooksLikeAddress(string token)
    {
        if (!IPAddress.TryParse(token, out IPAddress address))
            return false;

        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            || token.Count(character => character == '.') == 3;
    }

    /// <summary>
    ///     This method checks for one of the stock hosts-file names (see
    ///     <see cref="HostsFileBoilerplateNames" />).
    /// </summary>
    private static bool IsHostsFileBoilerplate(string name) =>
        HostsFileBoilerplateNames.Contains(name.TrimEnd('.'));
}
