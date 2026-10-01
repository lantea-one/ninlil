using Ninlil.Dhcp.Common.OptionCatalog;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Tests;

/// <summary>
///     This test class checks the invariants of <see cref="Dhcpv4OptionCatalog" /> and
///     <see cref="Dhcpv6OptionCatalog" /> that the admin console's value entry relies on: every
///     entry has a name and description, no tag or code repeats within a protocol, and the "use
///     this server's address" flag is set only on address-shaped options (and on at least one
///     option per protocol). It runs against the static catalogs in memory.
/// </summary>
public class DhcpOptionCatalogTests
{
    [Fact]
    public void Dhcpv4Catalog_IsNotEmpty_AndExpandsWellBeyondTheOriginalEighteenMemberEnum()
    {
        // Dhcpv4OptionTagEnum names 18 tags (Pad and End included); the catalog is meant to
        // cover many more configurable options than that.
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
        // The invariant above holds trivially if no entry sets the flag, so check that one does
        // (Domain Name Server, at least).
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
