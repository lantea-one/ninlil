using Ninlil.Dhcp.Common.OptionCatalog;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Tests;

/// <summary>
///     This test class proves the real data-integrity invariants <see cref="Dhcpv4OptionCatalog" />/
///     <see cref="Dhcpv6OptionCatalog" /> must hold for a future value-entry UI (a later sub-phase)
///     to be able to trust this catalog blindly -- a non-empty name/description on every entry, no
///     duplicate tag/code within either protocol's own list, and the "use this server's own
///     address" flag only ever set on a genuinely address-shaped option, never merely asserting the
///     catalog is non-empty.
/// </summary>
public class DhcpOptionCatalogTests
{
    [Fact]
    public void Dhcpv4Catalog_IsNotEmpty_AndExpandsWellBeyondTheOriginalEighteenMemberEnum()
    {
        // The original Dhcpv4OptionTagEnum named 18 members (16 real option tags plus the Pad/End
        // structural framing tags, which never appear in this catalog at all) -- this sub-phase's
        // own job is expanding real, operator-configurable option coverage well beyond that.
        Assert.True(
            Dhcpv4OptionCatalog.Entries.Count > 18,
            "The DHCPv4 catalog should expand well beyond the original 18-member enum."
        );
    }

    [Fact]
    public void Dhcpv6Catalog_IsNotEmpty()
    {
        Assert.NotEmpty(Dhcpv6OptionCatalog.Entries);
    }

    [Theory]
    [MemberData(nameof(BothCatalogs))]
    public void EveryEntry_HasANonEmptyNameAndDescription(
        IReadOnlyList<DhcpOptionCatalogEntry> catalog
    )
    {
        foreach (DhcpOptionCatalogEntry entry in catalog)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(entry.Name),
                $"Option code {entry.Code} has an empty name."
            );
            Assert.False(
                string.IsNullOrWhiteSpace(entry.Description),
                $"Option code {entry.Code} has an empty description."
            );
        }
    }

    [Theory]
    [MemberData(nameof(BothCatalogs))]
    public void NoTwoEntries_ShareTheSameCode(IReadOnlyList<DhcpOptionCatalogEntry> catalog)
    {
        List<int> codes = [.. catalog.Select(entry => entry.Code)];

        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(BothCatalogs))]
    public void ServerAddressEligible_IsOnlyEverTrueForAnAddressTypedOption(
        IReadOnlyList<DhcpOptionCatalogEntry> catalog
    )
    {
        DhcpOptionValueTypeEnum[] addressTypes =
        [
            DhcpOptionValueTypeEnum.IPv4Address,
            DhcpOptionValueTypeEnum.IPv4AddressList,
            DhcpOptionValueTypeEnum.IPv6Address,
            DhcpOptionValueTypeEnum.IPv6AddressList,
        ];

        foreach (
            DhcpOptionCatalogEntry entry in catalog.Where(entry => entry.IsServerAddressEligible)
        )
        {
            Assert.Contains(entry.ValueType, addressTypes);
        }
    }

    [Fact]
    public void Dhcpv4Catalog_ContainsAtLeastOneGenuineServerAddressEligibleEntry()
    {
        // A real, positive assertion -- not merely that the invariant above is never violated, but
        // that the flag is genuinely used at all (Domain Name Server, at minimum).
        Assert.Contains(Dhcpv4OptionCatalog.Entries, entry => entry.IsServerAddressEligible);
    }

    [Fact]
    public void Dhcpv6Catalog_ContainsAtLeastOneGenuineServerAddressEligibleEntry()
    {
        Assert.Contains(Dhcpv6OptionCatalog.Entries, entry => entry.IsServerAddressEligible);
    }

    [Fact]
    public void Dhcpv4Catalog_TagsFall_WithinTheRealByteRange()
    {
        Assert.All(Dhcpv4OptionCatalog.Entries, entry => Assert.InRange(entry.Code, 0, 255));
    }

    [Fact]
    public void Dhcpv6Catalog_CodesFall_WithinTheRealUInt16Range()
    {
        Assert.All(Dhcpv6OptionCatalog.Entries, entry => Assert.InRange(entry.Code, 0, 65535));
    }

    /// <summary>This property supplies both catalogs to the theory-driven tests above.</summary>
    public static IEnumerable<object[]> BothCatalogs()
    {
        yield return [Dhcpv4OptionCatalog.Entries];
        yield return [Dhcpv6OptionCatalog.Entries];
    }
}
