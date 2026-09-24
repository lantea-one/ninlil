import { WebhookAuthModeEnum } from '../../api-client';

/**
 * This constant contains, in the exact order they read best in a picker, the only 7 event type
 * names `WebhookDeliverySubscriber<TEvent>` (`Ninlil.Dns.Server.Webhooks`) is actually
 * registered against -- confirmed directly against `DnsServerEventTypeRegistry`
 * (`Ninlil.Dns.Server.Core/Events/Broker/DnsServerEventTypeRegistry.cs`), not guessed.
 * `WebhooksController`'s own validation accepts any registered name, including
 * `"DnsQueryReceived"`/`"DnsQueryAnswered"` (deliberately excluded here, and from this registry's
 * own delivery wiring, since those two fire on every single query and a subscription naming
 * either can never actually fire -- no delivery subscriber exists for them), but a subscription
 * naming either of those two would be accepted by the backend and then simply never deliver --
 * restricting this picker to the 7 curated, actually-delivering names avoids an operator ever
 * hitting that silent dead end from this UI.
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

/** This constant contains every auth mode a webhook subscription's own outbound delivery POST can use. */
export const WEBHOOK_AUTH_MODE_OPTIONS: readonly WebhookAuthModeEnum[] = [
  WebhookAuthModeEnum.None,
  WebhookAuthModeEnum.Bearer,
  WebhookAuthModeEnum.Basic,
];

/** This interface describes one auth-credential field a given {@link WebhookAuthModeEnum} requires. */
export interface WebhookCredentialFieldDefinition {
  /** The exact key this field is stored under in the `authCredentials` dictionary the backend expects. */
  key: string;

  /** The field's own human-readable label, shown both in the form and in the reveal-once dialog. */
  label: string;
}

/**
 * This constant maps each {@link WebhookAuthModeEnum} onto the auth-credential field(s) its own
 * shape requires -- confirmed directly against `WebhooksController.TryValidateAuthCredentials`,
 * not assumed: `Bearer` needs a single `BearerToken`, `Basic` needs `Username` and `Password`
 * together, `None` needs nothing. Mirrors `CREDENTIAL_FIELDS_BY_PROVIDER_TYPE`'s own precedent in
 * `cloud-provider-credential.model.ts` for a different secret-bearing entity type.
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

/** This function returns the ordered auth-credential field list a given auth mode's own shape requires. */
export function credentialFieldsForAuthMode(
  authMode: WebhookAuthModeEnum,
): readonly WebhookCredentialFieldDefinition[] {
  return CREDENTIAL_FIELDS_BY_AUTH_MODE[authMode] ?? [];
}

/**
 * This function converts one credential dictionary key (`"BearerToken"`, `"Username"`, ...) into
 * the matching, idiomatically-camelCase Reactive Forms control name (`"bearerToken"`,
 * `"username"`, ...) `WebhookFormComponent`'s own fixed form group declares -- the identical
 * transform `cloud-provider-credential.model.ts`'s own `toControlName` already establishes.
 */
export function toControlName(dictionaryKey: string): string {
  return dictionaryKey.length === 0
    ? dictionaryKey
    : dictionaryKey.charAt(0).toLowerCase() + dictionaryKey.slice(1);
}

/**
 * This function builds the exact `authCredentials` dictionary shape the backend expects
 * (`Dictionary<string, string>`, keyed by each field's own PascalCase name) out of a form's own
 * flat, camelCase raw value object, reading only the fields the selected auth mode's own shape
 * actually requires.
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

/** This function reports whether every field a built auth-credentials payload requires actually has a non-blank value. */
export function authCredentialsPayloadIsComplete(
  payload: Readonly<Record<string, string>>,
): boolean {
  return Object.values(payload).every((value) => value.trim().length > 0);
}

/** This function renders a webhook's own event type name list as one short, comma-joined summary string for a list row. */
export function summarizeEventTypeNames(
  eventTypeNames: readonly string[] | null | undefined,
): string {
  if (!eventTypeNames || eventTypeNames.length === 0) return '(none)';
  return eventTypeNames.join(', ');
}
