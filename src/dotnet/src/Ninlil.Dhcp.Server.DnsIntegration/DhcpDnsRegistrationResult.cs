// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration;

/// <summary>
///     This class carries what a single <see cref="DhcpDnsRegistrationService.HandleLeaseChangeAsync" />
///     call actually did, or why it did nothing -- mirroring
///     <c>Ninlil.Dns.Server.Authoritative.Ptr.PtrReconciliationResult</c>'s own "report the
///     outcome, don't just return void" shape.
/// </summary>
/// <param name="OwnerName">
///     The owner name a record was registered, updated, or removed for, or <see langword="null" />
///     when nothing was done.
/// </param>
/// <param name="WasSkipped">
///     Whether this pass did nothing at all -- the feature is disabled for this lease's own
///     scope, its designated zone is missing or ineligible, or the lease carries no usable host
///     name.
/// </param>
/// <param name="SkipReason">
///     A human-readable explanation of why nothing was done, or <see langword="null" /> when
///     <see cref="WasSkipped" /> is <see langword="false" />.
/// </param>
public record DhcpDnsRegistrationResult(string OwnerName, bool WasSkipped, string SkipReason)
{
    /// <summary>
    ///     This method builds a result reporting that nothing was done, and why.
    /// </summary>
    public static DhcpDnsRegistrationResult Skipped(string reason) => new(null, true, reason);

    /// <summary>
    ///     This method builds a result reporting that a new record was registered, or an existing
    ///     one was overwritten to point somewhere else.
    /// </summary>
    public static DhcpDnsRegistrationResult Registered(string ownerName) =>
        new(ownerName, false, null);

    /// <summary>
    ///     This method builds a result reporting that an existing record already matched exactly
    ///     what was desired, so no write was made at all.
    /// </summary>
    public static DhcpDnsRegistrationResult Unchanged(string ownerName) =>
        new(ownerName, false, null);

    /// <summary>
    ///     This method builds a result reporting that an existing record was removed.
    /// </summary>
    public static DhcpDnsRegistrationResult Removed(string ownerName) =>
        new(ownerName, false, null);
}
