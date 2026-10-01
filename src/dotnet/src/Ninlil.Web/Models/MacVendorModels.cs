using Ninlil.Dns.Server.ClientIdentity.Vendor;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the MAC vendor lookup settings (<c>MacVendorLookupOptions</c>) that
///     <c>GET /api/mac-vendor-settings</c> returns, with a read-only report of the vendor data
///     loaded. A <c>PATCH</c> to the same endpoint accepts every settings field except
///     <paramref name="DataFilePath" />, a path read once at startup that, like other state paths,
///     is set only in the configuration file.
/// </summary>
/// <param name="EnableAutomaticRefresh">Whether the IEEE registries are downloaded again periodically. Defaults to <see langword="true" />.</param>
/// <param name="RefreshInterval">How long to wait after a successful check before checking again. Defaults to 14 days.</param>
/// <param name="MaLRegistryUrl">Where the MA-L (24-bit) registry CSV is downloaded from.</param>
/// <param name="MaMRegistryUrl">Where the MA-M (28-bit) registry CSV is downloaded from.</param>
/// <param name="MaSRegistryUrl">Where the MA-S (36-bit) registry CSV is downloaded from.</param>
/// <param name="DataFilePath">Where refreshed data is persisted (read-only here).</param>
/// <param name="Status">The vendor data currently loaded, and the refresh history behind it.</param>
public record MacVendorSettingsDetail(
    bool EnableAutomaticRefresh,
    TimeSpan RefreshInterval,
    string MaLRegistryUrl,
    string MaMRegistryUrl,
    string MaSRegistryUrl,
    string DataFilePath,
    MacVendorDataStatusDetail Status
);

/// <summary>
///     This record reports the vendor data answering lookups and its refresh history. It is
///     returned inside <see cref="MacVendorSettingsDetail" /> and by an on-demand refresh
///     (<c>POST /api/mac-vendor-settings/refresh</c>).
/// </summary>
/// <param name="DataSource">Whether the loaded data is the snapshot compiled into this build or a downloaded copy.</param>
/// <param name="GeneratedAtUtc">When the loaded data was downloaded from the IEEE.</param>
/// <param name="MaLEntryCount">How many MA-L (24-bit) prefixes are loaded.</param>
/// <param name="MaMEntryCount">How many MA-M (28-bit) prefixes are loaded.</param>
/// <param name="MaSEntryCount">How many MA-S (36-bit) prefixes are loaded.</param>
/// <param name="TotalEntryCount">How many prefixes are loaded across all three registries.</param>
/// <param name="LastRefreshAttemptUtc">When a refresh was last attempted, or <see langword="null" /> if never.</param>
/// <param name="LastRefreshResult">That attempt's outcome, or <see langword="null" /> if never attempted.</param>
/// <param name="LastRefreshError">Why that attempt failed, or <see langword="null" /> when it did not.</param>
/// <param name="LastSuccessfulCheckUtc">When a refresh last completed successfully, or <see langword="null" /> if never.</param>
/// <param name="NextRefreshDueUtc">
///     When the next automatic refresh is due, or <see langword="null" /> while automatic refresh
///     is disabled.
/// </param>
public record MacVendorDataStatusDetail(
    MacVendorDataSourceEnum DataSource,
    DateTimeOffset GeneratedAtUtc,
    int MaLEntryCount,
    int MaMEntryCount,
    int MaSEntryCount,
    int TotalEntryCount,
    DateTimeOffset? LastRefreshAttemptUtc,
    MacVendorRefreshResultEnum? LastRefreshResult,
    string LastRefreshError,
    DateTimeOffset? LastSuccessfulCheckUtc,
    DateTimeOffset? NextRefreshDueUtc
);
