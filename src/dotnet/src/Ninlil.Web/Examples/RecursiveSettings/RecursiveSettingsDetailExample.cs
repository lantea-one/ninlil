using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.RecursiveSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="RecursiveSettingsDetail" />, referenced by
///     <see cref="Controllers.RecursiveSettingsController.Get" />.
/// </summary>
public class RecursiveSettingsDetailExample : IExamplesProvider<RecursiveSettingsDetail>
{
    /// <summary>
    ///     This method returns a realistic, deliberately-opted-in configuration: recursion,
    ///     DNSSEC validation, and QNAME minimization all on, this class's own compiled-in default
    ///     timeouts/bounds, no root-hints or trust-anchor override (the real internet root is
    ///     used), and automatic RFC 5011 trust anchor rollover left on.
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
