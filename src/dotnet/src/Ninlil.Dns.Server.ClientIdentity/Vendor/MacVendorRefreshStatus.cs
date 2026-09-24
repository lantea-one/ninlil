// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Vendor;

/// <summary>
///     This record is the refresh bookkeeping, persisted beside the data file so a restart knows
///     when the registries were last checked and never re-downloads them on every boot.
/// </summary>
/// <param name="LastAttemptUtc">When a refresh was last attempted, or <see langword="null" /> if never.</param>
/// <param name="LastResult">The outcome of that attempt, or <see langword="null" /> if never attempted.</param>
/// <param name="LastError">Why that attempt failed, or <see langword="null" /> when it did not.</param>
/// <param name="LastSuccessfulCheckUtc">
///     When a refresh last completed successfully (updated or confirmed unchanged), or
///     <see langword="null" /> if never.
/// </param>
public sealed record MacVendorRefreshStatus(
    DateTimeOffset? LastAttemptUtc,
    MacVendorRefreshResultEnum? LastResult,
    string LastError,
    DateTimeOffset? LastSuccessfulCheckUtc
)
{
    /// <summary>
    ///     This property contains the status of a server that has never attempted a refresh.
    /// </summary>
    public static MacVendorRefreshStatus Never { get; } = new(null, null, null, null);
}
