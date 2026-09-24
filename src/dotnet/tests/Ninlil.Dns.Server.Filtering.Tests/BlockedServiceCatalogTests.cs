using Ninlil.Dns.Server.Filtering.BlockedServices;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering.Tests;

/// <summary>
///     This class covers <see cref="BlockedServiceCatalog" />'s own structural invariants -- every
///     entry must have a stable, non-blank key, an <c>https://</c> subscription URL, and every key
///     must be unique, so <c>Ninlil.Web.Controllers.BlockedServicesController</c>'s own
///     key-based lookups can never be ambiguous.
/// </summary>
public class BlockedServiceCatalogTests
{
    [Fact]
    public void Entries_IsNeverEmpty()
    {
        Assert.NotEmpty(BlockedServiceCatalog.Entries);
    }

    [Fact]
    public void Entries_EveryKeyIsUnique()
    {
        List<string> keys = [.. BlockedServiceCatalog.Entries.Select(entry => entry.Key)];

        Assert.Equal(keys.Distinct(StringComparer.OrdinalIgnoreCase).Count(), keys.Count);
    }

    [Theory]
    [MemberData(nameof(EntryKeys))]
    public void Entries_EveryEntryHasARealHttpsSubscriptionUrl(string key)
    {
        BlockedServiceCatalogEntry entry = BlockedServiceCatalog.FindByKey(key);

        Assert.NotNull(entry);
        Assert.False(string.IsNullOrWhiteSpace(entry.DisplayName));
        Assert.False(string.IsNullOrWhiteSpace(entry.Description));
        Assert.StartsWith("https://", entry.SubscriptionUrl);
    }

    [Fact]
    public void FindByKey_IsCaseInsensitive()
    {
        BlockedServiceCatalogEntry entry = BlockedServiceCatalog.FindByKey("TikTok");

        Assert.NotNull(entry);
        Assert.Equal("tiktok", entry.Key);
    }

    [Fact]
    public void FindByKey_WithAnUnknownKey_ReturnsNull()
    {
        Assert.Null(BlockedServiceCatalog.FindByKey("not-a-real-service"));
    }

    public static IEnumerable<object[]> EntryKeys() =>
        BlockedServiceCatalog.Entries.Select(entry => new object[] { entry.Key });
}
