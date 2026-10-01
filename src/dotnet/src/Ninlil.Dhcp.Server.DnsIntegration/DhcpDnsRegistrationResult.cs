// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration;

/// <summary>
///     This record reports what one <see cref="DhcpDnsRegistrationService.HandleLeaseChangeAsync" />
///     call did, or why it did nothing, like <c>Ninlil.Dns.Server.Authoritative.Ptr.PtrReconciliationResult</c>.
///     Tests use it to check the outcome. Registered, unchanged and removed results look the same;
///     only <see cref="WasSkipped" /> tells skips apart.
/// </summary>
/// <param name="OwnerName">
///     The owner name a record was registered, updated, or removed for, or <see langword="null" />
///     when nothing was done.
/// </param>
/// <param name="WasSkipped">
///     Whether nothing was done: no DNS zone for the scope, a missing or ineligible zone, an
///     unusable address or host name, or no record to remove.
/// </param>
/// <param name="SkipReason">
///     An explanation of why nothing was done, or <see langword="null" /> when
///     <see cref="WasSkipped" /> is <see langword="false" />.
/// </param>
public record DhcpDnsRegistrationResult(string OwnerName, bool WasSkipped, string SkipReason)
{
    /// <summary>
    ///     This method builds a result reporting that nothing was done, and why.
    /// </summary>
    public static DhcpDnsRegistrationResult Skipped(string reason) => new(null, true, reason);

    /// <summary>
    ///     This method builds a result reporting that a record was added, possibly replacing an
    ///     older one.
    /// </summary>
    public static DhcpDnsRegistrationResult Registered(string ownerName) =>
        new(ownerName, false, null);

    /// <summary>
    ///     This method builds a result reporting that the record was already correct, so nothing
    ///     was written.
    /// </summary>
    public static DhcpDnsRegistrationResult Unchanged(string ownerName) =>
        new(ownerName, false, null);

    /// <summary>
    ///     This method builds a result reporting that an existing record was removed.
    /// </summary>
    public static DhcpDnsRegistrationResult Removed(string ownerName) =>
        new(ownerName, false, null);
}
