using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering.Tests;

/// <summary>
///     This class covers <see cref="DnsFilterListParser" /> line by line: every format a
///     subscribed domain list can be written in (hosts, bare domains, adblock syntax), mixes of
///     them, and every kind of line it must skip -- and whether that skip is counted.
/// </summary>
public class DnsFilterListParserTests
{
    private static DnsFilterListReplacementEntry Rule(string value) => new(value);

    private static DnsFilterListReplacementEntry Exception(string value) => new(value, true);

    [Theory]
    [InlineData("||ads.example.com^")]
    [InlineData("||ads.example.com^|")]
    [InlineData("||ads.example.com^$all")]
    [InlineData("||ads.example.com^$document")]
    [InlineData("||ads.example.com^$doc")]
    [InlineData("||ads.example.com^$DOCUMENT,all")]
    [InlineData("  ||ads.example.com^  ")]
    [InlineData("\t||ads.example.com^\r")]
    public void AdblockDomainRule_BecomesAnOrdinaryDomainEntry(string line)
    {
        DnsFilterListParseResult result = DnsFilterListParser.ParseDomainList(line);

        Assert.Equal([Rule("ads.example.com")], result.Entries);
        Assert.Equal(0, result.SkippedRuleCount);
    }

    [Fact]
    public void AdblockExceptionRule_BecomesAnExceptionEntry()
    {
        DnsFilterListParseResult result = DnsFilterListParser.ParseDomainList(
            "||tracker.example^\n@@||cdn.tracker.example^"
        );

        Assert.Equal([Rule("tracker.example"), Exception("cdn.tracker.example")], result.Entries);
        Assert.Equal(1, result.ExceptionCount);
    }

    [Fact]
    public void AdblockExceptionRule_InAnAllowList_BecomesAnOrdinaryEntry()
    {
        // An allowlist published in adblock syntax is written entirely in "@@" lines; in a list
        // whose own action is already Allow there is nothing for an exception to carve out of.
        DnsFilterListParseResult result = DnsFilterListParser.Parse(
            "@@||good.example^\n||also-good.example^",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Allow
        );

        Assert.Equal([Rule("good.example"), Rule("also-good.example")], result.Entries);
        Assert.Equal(0, result.ExceptionCount);
    }

    [Theory]
    [InlineData("! Title: AdGuard DNS filter")]
    [InlineData("!")]
    [InlineData("[Adblock Plus 2.0]")]
    [InlineData("[AdGuard]")]
    [InlineData("# a hosts-file comment")]
    [InlineData("#")]
    [InlineData("##.ad-banner")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r")]
    public void CommentsHeadersAndBlankLines_AreIgnoredWithoutBeingCounted(string line)
    {
        DnsFilterListParseResult result = DnsFilterListParser.ParseDomainList(line);

        Assert.Empty(result.Entries);
        Assert.Equal(0, result.SkippedRuleCount);
    }

    [Theory]
    [InlineData("||ads.example.com^$important")]
    [InlineData("||ads.example.com^$client=192.168.1.5")]
    [InlineData("||ads.example.com^$client='Kid laptop'")]
    [InlineData("||ads.example.com^$dnstype=AAAA")]
    [InlineData("||ads.example.com^$dnsrewrite=1.2.3.4")]
    [InlineData("||ads.example.com^$denyallow=cdn.example.com")]
    [InlineData("||ads.example.com^$badfilter")]
    [InlineData("||ads.example.com^$third-party")]
    [InlineData("||ads.example.com^$all,important")]
    [InlineData("||ads.example.com^$")]
    [InlineData("@@||ads.example.com^$important")]
    [InlineData("example.com##.ad-banner")]
    [InlineData("example.com#@#.ad-banner")]
    [InlineData("example.com#?#div:has(> .ad)")]
    [InlineData("||example.com/ads/banner.js")]
    [InlineData("||example.com/ads^")]
    [InlineData("|https://ads.example.com^")]
    [InlineData("|http://ads.example.com/")]
    [InlineData("/banner[0-9]+/")]
    [InlineData("/ads/tracker.js")]
    [InlineData("||ads.example.com")]
    [InlineData("||*.ads.example.com^")]
    [InlineData("||ads*.example.com^")]
    [InlineData("||ads.example.com:8080^")]
    [InlineData("ads.example.com^")]
    [InlineData("@@|https://ads.example.com^")]
    [InlineData("@@ads.example.com")]
    [InlineData("||^")]
    [InlineData("||ads..example.com^")]
    public void UnsupportedRules_AreSkippedAndCounted(string line)
    {
        DnsFilterListParseResult result = DnsFilterListParser.ParseDomainList(line);

        Assert.Empty(result.Entries);
        Assert.Equal(1, result.SkippedRuleCount);
    }

    [Theory]
    [InlineData("0.0.0.0 ads.example.com")]
    [InlineData("127.0.0.1 ads.example.com")]
    [InlineData(":: ads.example.com")]
    [InlineData("::1 ads.example.com")]
    [InlineData("::0 ads.example.com")]
    [InlineData("0.0.0.0\tads.example.com")]
    [InlineData("0.0.0.0   ads.example.com   ")]
    [InlineData("0.0.0.0 ads.example.com # a trailing comment")]
    [InlineData("0.0.0.0 ads.example.com #trailing comment with no space after the hash")]
    [InlineData("ads.example.com")]
    [InlineData("ads.example.com # bare domain with a comment")]
    public void HostsAndBareDomainLines_KeepWorking(string line)
    {
        DnsFilterListParseResult result = DnsFilterListParser.ParseDomainList(line);

        Assert.Equal([Rule("ads.example.com")], result.Entries);
        Assert.Equal(0, result.SkippedRuleCount);
    }

    [Fact]
    public void HostsLine_WithSeveralNames_KeepsEveryName()
    {
        DnsFilterListParseResult result = DnsFilterListParser.ParseDomainList(
            "0.0.0.0 ads.example.com tracker.example.com"
        );

        Assert.Equal([Rule("ads.example.com"), Rule("tracker.example.com")], result.Entries);
    }

    [Fact]
    public void StockHostsFileBoilerplate_IsDroppedWithoutBeingCounted()
    {
        const string content = """
            127.0.0.1 localhost
            127.0.0.1 localhost.localdomain
            127.0.0.1 local
            255.255.255.255 broadcasthost
            ::1 localhost ip6-localhost ip6-loopback
            fe80::1%lo0 localhost
            ff00::0 ip6-localnet
            ff00::0 ip6-mcastprefix
            ff02::1 ip6-allnodes
            ff02::2 ip6-allrouters
            ff02::3 ip6-allhosts
            0.0.0.0 0.0.0.0
            0.0.0.0 ads.example.com
            """;

        DnsFilterListParseResult result = DnsFilterListParser.ParseDomainList(content);

        Assert.Equal([Rule("ads.example.com")], result.Entries);
        Assert.Equal(0, result.SkippedRuleCount);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("192.0.2.1")]
    [InlineData("not a hosts line")]
    [InlineData("0.0.0.0 bad/name.example")]
    [InlineData("ads.example.com/path")]
    [InlineData("*.ads.example.com")]
    public void MalformedHostsAndBareLines_AreSkippedAndCounted(string line)
    {
        DnsFilterListParseResult result = DnsFilterListParser.ParseDomainList(line);

        Assert.Empty(result.Entries);
        Assert.Equal(1, result.SkippedRuleCount);
    }

    [Theory]
    [InlineData("||ADS.Example.COM^", "ads.example.com")]
    [InlineData("0.0.0.0 ADS.EXAMPLE.COM", "ads.example.com")]
    [InlineData("Ads.Example.Com", "ads.example.com")]
    [InlineData("||ads.example.com.^", "ads.example.com")]
    [InlineData("0.0.0.0 ads.example.com.", "ads.example.com")]
    [InlineData("ads.example.com.", "ads.example.com")]
    [InlineData("@@||CDN.Example.COM.^", "cdn.example.com")]
    [InlineData("||_dmarc.example.com^", "_dmarc.example.com")]
    [InlineData("||zip^", "zip")]
    public void Names_AreLowercasedAndLoseTheirTrailingDot(string line, string expected)
    {
        DnsFilterListParseResult result = DnsFilterListParser.ParseDomainList(line);

        Assert.Single(result.Entries);
        Assert.Equal(expected, result.Entries[0].Value);
    }

    [Theory]
    [InlineData("||bücher.example^", "xn--bcher-kva.example")]
    [InlineData("||BÜCHER.example^", "xn--bcher-kva.example")]
    [InlineData("0.0.0.0 bücher.example", "xn--bcher-kva.example")]
    [InlineData("пример.рф", "xn--e1afmkfd.xn--p1ai")]
    [InlineData("||xn--bcher-kva.example^", "xn--bcher-kva.example")]
    [InlineData("||XN--BCHER-KVA.example^", "xn--bcher-kva.example")]
    public void InternationalizedNames_AreStoredAsPunycode(string line, string expected)
    {
        DnsFilterListParseResult result = DnsFilterListParser.ParseDomainList(line);

        Assert.Equal([Rule(expected)], result.Entries);
    }

    [Fact]
    public void MixedFormatList_WithCrlfLineEndings_ParsesEveryFormatTogether()
    {
        string content = string.Join(
            "\r\n",
            "﻿[Adblock Plus 2.0]",
            "! Title: Mixed test list",
            "! Expires: 1 day",
            "# hosts-style comment",
            "",
            "||ads.example.com^",
            "@@||good.ads.example.com^",
            "0.0.0.0 tracker.example.net",
            "127.0.0.1 metrics.example.org # telemetry",
            ":: v6only.example",
            "bare.example.io",
            "||important.example^$important",
            "example.com##.banner",
            "||ads.example.com^",
            "0.0.0.0 ads.example.com",
            "||client-scoped.example^$client=10.0.0.5",
            "/regex-rule[0-9]/",
            "   "
        );

        DnsFilterListParseResult result = DnsFilterListParser.ParseDomainList(content);

        Assert.Equal(
            [
                Rule("ads.example.com"),
                Exception("good.ads.example.com"),
                Rule("tracker.example.net"),
                Rule("metrics.example.org"),
                Rule("v6only.example"),
                Rule("bare.example.io"),
            ],
            result.Entries
        );
        Assert.Equal(1, result.ExceptionCount);
        Assert.Equal(4, result.SkippedRuleCount);
    }

    [Fact]
    public void SameNameAsRuleAndException_KeepsBoth()
    {
        DnsFilterListParseResult result = DnsFilterListParser.ParseDomainList(
            "||x.example^\n@@||x.example^\n||x.example^\n@@||x.example^"
        );

        Assert.Equal([Rule("x.example"), Exception("x.example")], result.Entries);
    }

    [Fact]
    public void EmptyOrNullContent_ProducesNothing()
    {
        Assert.Empty(DnsFilterListParser.ParseDomainList(string.Empty).Entries);
        Assert.Empty(DnsFilterListParser.ParseDomainList(null).Entries);
    }

    [Fact]
    public void NetworkList_TakesTheFirstTokenAndIgnoresAnnotations()
    {
        const string content = """
            ; Spamhaus DROP List
            # comment
            ! comment
            1.10.16.0/20 ; SBL256894
            198.51.100.23
            2001:db8::/32 # documentation range
            2001:DB8::1
            198.51.100.23
            not-an-address
            10.1
            """;

        DnsFilterListParseResult result = DnsFilterListParser.Parse(
            content,
            DnsFilterListKindEnum.ResponseIp,
            DnsFilterListActionEnum.Block
        );

        Assert.Equal(
            [
                Rule("1.10.16.0/20"),
                Rule("198.51.100.23"),
                Rule("2001:db8::/32"),
                Rule("2001:db8::1"),
            ],
            result.Entries
        );
        Assert.Equal(2, result.SkippedRuleCount);
    }

    [Fact]
    public void ClientSourceList_UsesTheNetworkFormatToo()
    {
        DnsFilterListParseResult result = DnsFilterListParser.Parse(
            "192.0.2.0/24\n||ads.example.com^",
            DnsFilterListKindEnum.ClientSource,
            DnsFilterListActionEnum.Block
        );

        Assert.Equal([Rule("192.0.2.0/24")], result.Entries);
        Assert.Equal(1, result.SkippedRuleCount);
    }
}
