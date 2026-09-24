using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Rewrites.SafeSearch;

// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites.Tests;

/// <summary>
///     This class covers <see cref="SafeSearchCatalog" />'s own structural invariants -- every
///     entry must have a stable, non-blank key, at least one hostname mapping, and every mapping's
///     own target must actually classify as CNAME (never an IP literal, which
///     <c>DnsRewriteEngine</c> would answer with a completely different record type than a real
///     safe-search CNAME target ever needs) via the real
///     <see cref="DnsRewriteTargetClassifier" /> -- the same classifier
///     <c>Ninlil.Web.Controllers.SafeSearchController</c> and <c>DnsRewriteEngine</c> itself
///     both use.
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
