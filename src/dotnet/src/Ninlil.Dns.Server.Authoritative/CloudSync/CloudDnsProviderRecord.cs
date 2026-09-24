using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CloudSync;

/// <summary>
///     This class pairs a cloud DNS provider's own record identifier with that record's decoded
///     wire-format equivalent -- the provider-side identifier is opaque to
///     <see cref="CloudDnsZoneSyncService" />'s own reconciliation logic (which matches records
///     purely by content, the same way <c>SecondaryZoneRefreshService</c>'s AXFR reconciliation
///     does), but <see cref="ICloudDnsProvider.DeleteRecordAsync" /> needs it back to know which
///     specific remote record to delete.
/// </summary>
/// <param name="ProviderRecordId">The provider's own identifier for this record.</param>
/// <param name="Record">This record, decoded into Ninlil's own wire-format model.</param>
public sealed record CloudDnsProviderRecord(string ProviderRecordId, DnsResourceRecord Record);
