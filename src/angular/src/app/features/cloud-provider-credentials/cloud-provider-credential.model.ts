import { CloudDnsProviderTypeEnum } from '../../api-client';

/** This interface describes one secret field a provider type's credentials need. */
export interface CredentialFieldDefinition {
  /** The key of this field in the `credentials` dictionary the backend expects. */
  key: string;

  /** The label shown in the form and in the reveal-once dialog. */
  label: string;
}

/**
 * This constant maps each {@link CloudDnsProviderTypeEnum} to the secret fields its credentials
 * need: one `ApiToken` for Cloudflare and DigitalOcean, `AccessKeyId` and `SecretAccessKey` for
 * Route 53. It must match the backend's `CloudCredentialValidator`, which rejects a set missing a
 * field with a 400.
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

/** This function returns the credential fields a provider type needs, in display order. */
export function credentialFieldsFor(
  providerType: CloudDnsProviderTypeEnum,
): readonly CredentialFieldDefinition[] {
  return CREDENTIAL_FIELDS_BY_PROVIDER_TYPE[providerType] ?? [];
}

/**
 * This function turns a credential key (`"ApiToken"`, `"AccessKeyId"`, ...) into the form control
 * name `CloudProviderCredentialFormComponent` uses (`"apiToken"`, `"accessKeyId"`, ...), by
 * lowercasing the first letter. Deriving it avoids a second map that could drift from
 * {@link CREDENTIAL_FIELDS_BY_PROVIDER_TYPE}.
 */
export function toControlName(dictionaryKey: string): string {
  return dictionaryKey.length === 0
    ? dictionaryKey
    : dictionaryKey.charAt(0).toLowerCase() + dictionaryKey.slice(1);
}

/**
 * This function builds the `credentials` dictionary the backend expects (keyed by PascalCase
 * name) from the form's camelCase values, reading only the fields the provider type needs. A
 * stray value left in another provider's field, after switching type, is not sent.
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

/** This function reports whether every field in a built credentials payload is non-blank. */
export function credentialsPayloadIsComplete(payload: Readonly<Record<string, string>>): boolean {
  return Object.values(payload).every((value) => value.trim().length > 0);
}
