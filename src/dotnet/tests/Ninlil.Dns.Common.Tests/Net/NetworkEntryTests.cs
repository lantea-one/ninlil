using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Net;

// Define our namespace.
namespace Ninlil.Dns.Common.Tests.Net;

/// <summary>
///     This class tests <see cref="NetworkEntry" /> with in-memory addresses. The parity tests
///     compare it, for every entry form and address form in the tables below, with a verbatim
///     copy of the matching algorithm that parsed the entry text on every call; a rule change in
///     <see cref="NetworkEntry" /> that moves any pair fails them.
/// </summary>
public sealed class NetworkEntryTests
{
    /// <summary>
    ///     This field is every entry form the product accepts or rejects: IPv4 and IPv6 networks,
    ///     bare addresses with and without spaces, IPv4-mapped forms, host bits set, scope ids,
    ///     prefix edge cases, and text that does not parse.
    /// </summary>
    public static readonly string[] Entries =
    [
        "192.0.2.0/24",
        "192.0.2.5/24",
        "192.0.2.5/32",
        "0.0.0.0/0",
        "::/0",
        "10.0.0.0/8",
        "192.0.2.0/99",
        "192.0.2.9",
        "  192.0.2.9  ",
        " 192.0.2.0/24 ",
        "192.0.2.0 /24",
        "::ffff:192.0.2.9",
        "::ffff:192.0.2.0/120",
        "::ffff:192.0.2.0/104",
        "::ffff:0.0.0.0/96",
        "2001:db8::/32",
        "2001:db8::5",
        "2001:db8::5/128",
        "fe80::1",
        "fe80::1%2",
        "fe80::/10",
        "fe80::1%2/64",
        "fd00::/8",
        "not an address",
        "/24",
        "192.0.2.0/",
        "192.0.2.0/-1",
        "1/2/3",
        "",
        "   ",
        null,
    ];

    /// <summary>
    ///     This field is every address form a listener can report: plain, IPv4-mapped, zone-indexed,
    ///     boundary addresses of the networks above, and unrelated ones.
    /// </summary>
    public static readonly string[] Addresses =
    [
        "192.0.2.0",
        "192.0.2.5",
        "192.0.2.9",
        "192.0.2.255",
        "192.0.3.1",
        "10.1.2.3",
        "0.0.0.0",
        "255.255.255.255",
        "::ffff:192.0.2.9",
        "::ffff:192.0.2.5",
        "::ffff:192.0.3.1",
        "::ffff:10.1.2.3",
        "::",
        "::1",
        "2001:db8::5",
        "2001:db8:1::5",
        "fe80::1",
        "fe80::1%2",
        "fe80::1%7",
        "fe80::abcd%2",
        "fd00::1",
        "fc00::1",
    ];

    /// <summary>
    ///     This method is the matching algorithm as it parsed the entry text on every call. It is
    ///     the reference <see cref="NetworkEntry" /> must equal.
    /// </summary>
    private static bool ReferenceNetworkContains(string entry, IPAddress address)
    {
        if (string.IsNullOrWhiteSpace(entry) || address == null)
            return false;

        IPAddress normalizedAddress = ClientAddress.Normalize(address);

        if (entry.Contains('/'))
        {
            if (!IPNetwork.TryParse(entry, out IPNetwork network))
                return false;

            IPAddress candidate =
                network.BaseAddress.IsIPv4MappedToIPv6
                && normalizedAddress.AddressFamily == AddressFamily.InterNetwork
                    ? normalizedAddress.MapToIPv6()
                    : normalizedAddress;

            return network.Contains(candidate);
        }

        return IPAddress.TryParse(entry.Trim(), out IPAddress parsed)
            && ClientAddress.Normalize(parsed).Equals(normalizedAddress);
    }

    public static IEnumerable<object[]> EntryData() =>
        Entries.Select(entry => new object[] { entry });

    [Theory]
    [MemberData(nameof(EntryData))]
    public void Contains_ForEveryAddressForm_EqualsTheParsePerCallAlgorithm(string entry)
    {
        bool parsed = NetworkEntry.TryParse(entry, out NetworkEntry network);

        foreach (string text in Addresses)
        {
            IPAddress address = IPAddress.Parse(text);
            bool expected = ReferenceNetworkContains(entry, address);
            bool actual = parsed && network.Contains(address);

            Assert.True(expected == actual, $"entry '{entry}', address '{text}'");
            Assert.True(
                expected == ClientAddress.NetworkContains(entry, address),
                $"NetworkContains: entry '{entry}', address '{text}'"
            );
        }
    }

    [Theory]
    [MemberData(nameof(EntryData))]
    public void TryParse_AnEntryThatDoesNotParse_MatchesNoAddressInTheReference(string entry)
    {
        if (NetworkEntry.TryParse(entry, out NetworkEntry network))
        {
            Assert.NotNull(network);
            return;
        }

        Assert.Null(network);

        foreach (string text in Addresses)
            Assert.False(ReferenceNetworkContains(entry, IPAddress.Parse(text)));
    }

    [Fact]
    public void Contains_ANullAddress_MatchesNothing()
    {
        Assert.True(NetworkEntry.TryParse("192.0.2.0/24", out NetworkEntry network));
        Assert.False(network.Contains(null));

        Assert.True(NetworkEntry.TryParse("192.0.2.9", out NetworkEntry bare));
        Assert.False(bare.Contains(null));
    }

    [Theory]
    [InlineData("::ffff:192.0.2.0/120", "192.0.2.5", true)]
    [InlineData("::ffff:192.0.2.0/120", "192.0.3.5", false)]
    [InlineData("192.0.2.0/24", "::ffff:192.0.2.5", true)]
    [InlineData("  192.0.2.9 ", "::ffff:192.0.2.9", true)]
    [InlineData("fe80::1", "fe80::1%4", true)]
    [InlineData("192.0.2.5/24", "192.0.2.77", true)]
    [InlineData("0.0.0.0/0", "203.0.113.1", true)]
    public void Contains_SpotChecks_GiveTheDocumentedAnswer(
        string entry,
        string address,
        bool expected
    )
    {
        Assert.True(NetworkEntry.TryParse(entry, out NetworkEntry network));
        Assert.Equal(expected, network.Contains(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("not an address")]
    [InlineData("192.0.2.0/99")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParse_TextThatIsNotAnAddressOrNetwork_ReturnsFalseAndNull(string entry)
    {
        Assert.False(NetworkEntry.TryParse(entry, out NetworkEntry network));
        Assert.Null(network);
    }
}
