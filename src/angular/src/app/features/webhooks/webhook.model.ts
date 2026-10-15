import { WebhookAuthModeEnum } from '../../api-client';

/**
 * This constant lists the event types the webhook form offers: the seven that have a
 * `WebhookDeliverySubscriber<TEvent>` registered (`ServiceCollectionWebhooksExtension`), in the
 * order they read best. The list mirrors the server's webhook event catalog
 * (`WebhookEventCatalog`), and the server answers 400 for every other event type name, so the form
 * must not offer one.
 */
export const CURATED_WEBHOOK_EVENT_TYPE_NAMES: readonly string[] = [
  'DnsZoneChanged',
  'DnsCacheEvicted',
  'DnssecZoneResigningCompleted',
  'AcmeCertificateRenewalSucceeded',
  'AcmeCertificateRenewalFailed',
  'CloudDnsZoneSyncFailed',
  'TrustAnchorRollover',
] as const;

/** This constant lists the auth modes a webhook delivery `POST` can use. */
export const WEBHOOK_AUTH_MODE_OPTIONS: readonly WebhookAuthModeEnum[] = [
  WebhookAuthModeEnum.None,
  WebhookAuthModeEnum.Bearer,
  WebhookAuthModeEnum.Basic,
];

/** This interface describes one credential field an auth mode needs. */
export interface WebhookCredentialFieldDefinition {
  /** The key of this field in the `authCredentials` dictionary the backend expects. */
  key: string;

  /** The label shown in the form and in the reveal-once dialog. */
  label: string;
}

/**
 * This constant maps each {@link WebhookAuthModeEnum} to its credential fields: `BearerToken` for
 * Bearer, `Username` and `Password` for Basic, none for None. It must match
 * `WebhooksController.TryValidateAuthCredentials`.
 */
export const CREDENTIAL_FIELDS_BY_AUTH_MODE: Readonly<
  Record<WebhookAuthModeEnum, readonly WebhookCredentialFieldDefinition[]>
> = {
  [WebhookAuthModeEnum.None]: [],
  [WebhookAuthModeEnum.Bearer]: [{ key: 'BearerToken', label: 'Bearer token' }],
  [WebhookAuthModeEnum.Basic]: [
    { key: 'Username', label: 'Username' },
    { key: 'Password', label: 'Password' },
  ],
};

/** This function returns the credential fields an auth mode needs, in display order. */
export function credentialFieldsForAuthMode(
  authMode: WebhookAuthModeEnum,
): readonly WebhookCredentialFieldDefinition[] {
  return CREDENTIAL_FIELDS_BY_AUTH_MODE[authMode] ?? [];
}

/**
 * This function turns a credential key (`"BearerToken"`, `"Username"`, ...) into the form control
 * name `WebhookFormComponent` uses (`"bearerToken"`, `"username"`, ...).
 */
export function toControlName(dictionaryKey: string): string {
  return dictionaryKey.length === 0
    ? dictionaryKey
    : dictionaryKey.charAt(0).toLowerCase() + dictionaryKey.slice(1);
}

/**
 * This function builds the `authCredentials` dictionary (keyed by PascalCase name) from the
 * form's camelCase values, reading only the fields the auth mode needs.
 */
export function buildAuthCredentialsPayload(
  authMode: WebhookAuthModeEnum,
  values: Readonly<Record<string, string>>,
): Record<string, string> {
  const payload: Record<string, string> = {};
  for (const field of credentialFieldsForAuthMode(authMode)) {
    payload[field.key] = values[toControlName(field.key)] ?? '';
  }
  return payload;
}

/** This function reports whether every field in a built credentials payload is non-blank. */
export function authCredentialsPayloadIsComplete(
  payload: Readonly<Record<string, string>>,
): boolean {
  return Object.values(payload).every((value) => value.trim().length > 0);
}

/** This function joins a webhook's event type names into one comma-separated string for its list row. */
export function summarizeEventTypeNames(
  eventTypeNames: readonly string[] | null | undefined,
): string {
  if (!eventTypeNames || eventTypeNames.length === 0) return '(none)';
  return eventTypeNames.join(', ');
}
