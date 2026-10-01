using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.RecursiveSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="RecursiveSettingsDetail" />, as
///     returned by <see cref="Controllers.RecursiveSettingsController.Get" />.
/// </summary>
public class RecursiveSettingsDetailExample : IExamplesProvider<RecursiveSettingsDetail>
{
    /// <summary>
    ///     This method returns recursion turned on; every other field is the default, including
    ///     DNSSEC validation, QNAME minimization and RFC 5011 trust anchor updates on, and no root
    ///     hint or trust anchor overrides (so the internet root is used).
    /// </summary>
    public RecursiveSettingsDetail GetExamples() =>
        new(
            EnableRecursion: true,
            EnableDnssecValidation: true,
            EnableQnameMinimization: true,
            UpstreamQueryTimeout: TimeSpan.FromSeconds(3),
            OverallQueryTimeout: TimeSpan.FromSeconds(15),
            MaxReferralDepth: 30,
            MaxCnameChainLength: 15,
            MaxUpstreamQueries: 100,
            MaxCacheEntries: 100_000,
            RootHintServersOverride: [],
            TrustAnchorsOverride: [],
            EnableAutomaticTrustAnchorUpdate: true,
            TrustAnchorRefreshInterval: TimeSpan.FromDays(15),
            TrustAnchorHoldDownDuration: TimeSpan.FromDays(30),
            TrustAnchorRemoveHoldDownDuration: TimeSpan.FromDays(30),
            EnableServeStale: true,
            MaxStaleServeAge: TimeSpan.FromHours(24),
            EnablePrefetch: true,
            PrefetchWindow: TimeSpan.FromSeconds(10),
            PrefetchRecentAccessWindow: TimeSpan.FromMinutes(5)
        );
}
