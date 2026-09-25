using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CloudSync;

/// <summary>
///     This record pairs a provider's record ID with the decoded record.
///     <see cref="CloudDnsZoneSyncService" /> matches records by content, but
///     <see cref="ICloudDnsProvider.DeleteRecordAsync" /> needs the ID to delete one.
/// </summary>
/// <param name="ProviderRecordId">The provider's ID for the record.</param>
/// <param name="Record">The record in Ninlil's wire model.</param>
public sealed record CloudDnsProviderRecord(string ProviderRecordId, DnsResourceRecord Record);
