using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Rewrites.SafeSearch;

// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites.Tests;

/// <summary>
///     This class checks the shape of the static <see cref="SafeSearchCatalog" /> data: unique keys,
///     filled-in names and documentation links, at least one mapping per entry, no duplicate source
///     domains, and every target classifying as a CNAME through
///     <see cref="DnsRewriteTargetClassifier" /> (an IP target would be answered as A/AAAA). It
///     also covers case-insensitive <see cref="SafeSearchCatalog.FindByKey" />.
/// </summary>
public class SafeSearchCatalogTests
{
    [Fact]
    public void Entries_IsNeverEmpty()
    {
        Assert.NotEmpty(SafeSearchCatalog.Entries);
    }

    [Fact]
    public void Entries_EveryKeyIsUnique()
    {
        List<string> keys = [.. SafeSearchCatalog.Entries.Select(entry => entry.Key)];

        Assert.Equal(keys.Distinct(StringComparer.OrdinalIgnoreCase).Count(), keys.Count);
    }

    public static IEnumerable<object[]> EntryKeys() =>
        SafeSearchCatalog.Entries.Select(entry => new object[] { entry.Key });

    [Theory]
    [MemberData(nameof(EntryKeys))]
    public void Entries_EveryEntryHasAtLeastOneMappingAndAllTargetsClassifyAsCname(string key)
    {
        SafeSearchCatalogEntry entry = SafeSearchCatalog.FindByKey(key);

        Assert.NotNull(entry);
        Assert.False(string.IsNullOrWhiteSpace(entry.DisplayName));
        Assert.False(string.IsNullOrWhiteSpace(entry.Description));
        Assert.StartsWith("https://", entry.SourceDocumentationUrl);
        Assert.NotEmpty(entry.Mappings);

        foreach (SafeSearchRewriteMapping mapping in entry.Mappings)
        {
            Assert.False(string.IsNullOrWhiteSpace(mapping.SourceDomain));
            Assert.True(
                DnsRewriteTargetClassifier.TryClassify(
                    mapping.TargetDomain,
                    out Ninlil.Dns.Common.Enum.DnsRewriteTargetKindEnum kind,
                    out string error
                ),
                error
            );
            Assert.Equal(Ninlil.Dns.Common.Enum.DnsRewriteTargetKindEnum.Cname, kind);
        }
    }

    [Fact]
    public void Entries_NoTwoMappingsWithinTheSameEntryShareASourceDomain()
    {
        foreach (SafeSearchCatalogEntry entry in SafeSearchCatalog.Entries)
        {
            List<string> sources =
            [
                .. entry.Mappings.Select(mapping => mapping.SourceDomain.ToLowerInvariant()),
            ];

            Assert.Equal(sources.Distinct().Count(), sources.Count);
        }
    }

    [Fact]
    public void FindByKey_IsCaseInsensitive()
    {
        SafeSearchCatalogEntry entry = SafeSearchCatalog.FindByKey("Google");

        Assert.NotNull(entry);
        Assert.Equal("google", entry.Key);
    }

    [Fact]
    public void FindByKey_WithAnUnknownKey_ReturnsNull()
    {
        Assert.Null(SafeSearchCatalog.FindByKey("not-a-real-engine"));
    }
}
