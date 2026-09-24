import { CloudDnsProviderTypeEnum } from '../../api-client';

/** This interface describes one secret-material field a given provider type's own credential shape requires. */
export interface CredentialFieldDefinition {
  /** The exact key this field is stored under in the `credentials` dictionary the backend expects. */
  key: string;

  /** The field's own human-readable label, shown both in the form and in the reveal-once dialog. */
  label: string;
}

/**
 * This constant maps each {@link CloudDnsProviderTypeEnum} onto the secret-material field(s) its
 * own credential shape requires -- confirmed directly against
 * `CloudProviderCredentialsController.TryValidateCredentials`, not assumed from each provider's
 * own public API documentation: Cloudflare and DigitalOcean both authenticate with a single
 * `ApiToken`, while Route 53 needs an `AccessKeyId` *and* a `SecretAccessKey` together. Mirrored
 * here (not reused from the backend) since this is a client-side presentation concern, the same
 * "mirror, don't share a dependency across the REST boundary" precedent the backend's own
 * `TryValidateCredentials` doc comment already establishes for itself relative to
 * `ICloudDnsProvider`'s own sync-time checks.
 */
export const CREDENTIAL_FIELDS_BY_PROVIDER_TYPE: Readonly<
  Record<CloudDnsProviderTypeEnum, readonly CredentialFieldDefinition[]>
> = {
  [CloudDnsProviderTypeEnum.Cloudflare]: [{ key: 'ApiToken', label: 'API token' }],
  [CloudDnsProviderTypeEnum.DigitalOcean]: [{ key: 'ApiToken', label: 'API token' }],
  [CloudDnsProviderTypeEnum.Route53]: [
    { key: 'AccessKeyId', label: 'Access key ID' },
    { key: 'SecretAccessKey', label: 'Secret access key' },
  ],
};

/** This function returns the ordered credential field list a given provider type's own shape requires. */
export function credentialFieldsFor(
  providerType: CloudDnsProviderTypeEnum,
): readonly CredentialFieldDefinition[] {
  return CREDENTIAL_FIELDS_BY_PROVIDER_TYPE[providerType] ?? [];
}

/**
 * This function converts one credential dictionary key (`"ApiToken"`, `"AccessKeyId"`, ...) into
 * the matching, idiomatically-camelCase Reactive Forms control name (`"apiToken"`,
 * `"accessKeyId"`, ...) `CloudProviderCredentialFormComponent`'s own fixed form group declares --
 * a small, pure, reversible transform rather than a second, hand-maintained key-to-control-name
 * map that could silently drift out of sync with {@link CREDENTIAL_FIELDS_BY_PROVIDER_TYPE}.
 */
export function toControlName(dictionaryKey: string): string {
  return dictionaryKey.length === 0
    ? dictionaryKey
    : dictionaryKey.charAt(0).toLowerCase() + dictionaryKey.slice(1);
}

/**
 * This function builds the exact `credentials` dictionary shape the backend expects
 * (`Dictionary<string, string>`, keyed by each field's own PascalCase name) out of a form's own
 * flat, camelCase raw value object, reading only the fields the selected provider type's own
 * shape actually requires -- a Route 53 form's own unused `apiToken` control, for instance, is
 * never included even if a caller left a stray value in it from switching provider types.
 */
export function buildCredentialsPayload(
  providerType: CloudDnsProviderTypeEnum,
  values: Readonly<Record<string, string>>,
): Record<string, string> {
  const payload: Record<string, string> = {};
  for (const field of credentialFieldsFor(providerType)) {
    payload[field.key] = values[toControlName(field.key)] ?? '';
  }
  return payload;
}

/** This function reports whether every field a built credentials payload requires actually has a non-blank value. */
export function credentialsPayloadIsComplete(payload: Readonly<Record<string, string>>): boolean {
  return Object.values(payload).every((value) => value.trim().length > 0);
}
