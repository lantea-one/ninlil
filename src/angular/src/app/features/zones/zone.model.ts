import { ZoneTypeEnum } from '../../api-client';

/**
 * This constant lists every {@link ZoneTypeEnum} value in a fixed, curated display order (not
 * alphabetical) for the zone-type select in `ZoneFormComponent` -- primary/secondary roles first,
 * then forwarding, then the cloud-connector roles, matching the order an operator most often
 * considers them in.
 */
export const ZONE_TYPE_OPTIONS: readonly ZoneTypeEnum[] = [
  ZoneTypeEnum.Primary,
  ZoneTypeEnum.Secondary,
  ZoneTypeEnum.Stub,
  ZoneTypeEnum.Forwarder,
  ZoneTypeEnum.SecondaryForwarder,
  ZoneTypeEnum.Catalog,
  ZoneTypeEnum.SecondaryCatalog,
  ZoneTypeEnum.CloudPrimary,
  ZoneTypeEnum.CloudSecondary,
];

/**
 * This function reports whether a zone type transfers its own content *from* another server,
 * requiring {@link ZoneWriteRequest.PrimaryServerAddresses}: a secondary, a stub (which transfers
 * only the delegation-relevant records), or either of their catalog/forwarder-flavored cousins.
 * Read directly off `ZoneEntity`/`ZonesController`'s own accepted field set, not guessed.
 */
export function requiresPrimaryServerAddresses(type: ZoneTypeEnum): boolean {
  return (
    type === ZoneTypeEnum.Secondary ||
    type === ZoneTypeEnum.Stub ||
    type === ZoneTypeEnum.SecondaryForwarder ||
    type === ZoneTypeEnum.SecondaryCatalog
  );
}

/** This function reports whether a zone type forwards every query it receives to configured upstream addresses. */
export function requiresForwarderAddresses(type: ZoneTypeEnum): boolean {
  return type === ZoneTypeEnum.Forwarder || type === ZoneTypeEnum.SecondaryForwarder;
}

/**
 * This function reports whether a zone type syncs against a cloud DNS provider (via the
 * backend's `CloudSync/` connectors), requiring `CloudProviderCredentialId`/`CloudProviderZoneId`.
 */
export function isCloudZoneType(type: ZoneTypeEnum): boolean {
  return type === ZoneTypeEnum.CloudPrimary || type === ZoneTypeEnum.CloudSecondary;
}

/**
 * This function reports whether a zone type is one this server itself originates content for
 * (as opposed to transferring or forwarding it from elsewhere) -- the only zone types eligible
 * for locally-generated DNSSEC signing, RFC 2136 dynamic updates, and the backend's own CAA/PTR
 * auto-management, per `CaaRecordReconciliationService.IsEligibleZoneType`/
 * `PtrSyncService.IsEligibleZoneType` (both scope themselves to exactly `Primary`/`CloudPrimary`,
 * confirmed by reading each directly rather than assumed).
 */
export function isLocallyOriginatedZoneType(type: ZoneTypeEnum): boolean {
  return type === ZoneTypeEnum.Primary || type === ZoneTypeEnum.CloudPrimary;
}
