import { WebhookAuthModeEnum } from '../../api-client';
import {
  CURATED_WEBHOOK_EVENT_TYPE_NAMES,
  authCredentialsPayloadIsComplete,
  buildAuthCredentialsPayload,
  credentialFieldsForAuthMode,
  summarizeEventTypeNames,
  toControlName,
} from './webhook.model';

/** These tests cover the event type list, the credential fields per auth mode and the helpers. */
describe('webhook.model', () => {
  it('CURATED_WEBHOOK_EVENT_TYPE_NAMES contains exactly the 7 actually-delivering event types', () => {
    expect(CURATED_WEBHOOK_EVENT_TYPE_NAMES).toEqual([
      'DnsZoneChanged',
      'DnsCacheEvicted',
      'DnssecZoneResigningCompleted',
      'AcmeCertificateRenewalSucceeded',
      'AcmeCertificateRenewalFailed',
      'CloudDnsZoneSyncFailed',
      'TrustAnchorRollover',
    ]);
  });

  it('credentialFieldsForAuthMode() returns no fields for None', () => {
    expect(credentialFieldsForAuthMode(WebhookAuthModeEnum.None)).toEqual([]);
  });

  it('credentialFieldsForAuthMode() returns a single BearerToken field for Bearer', () => {
    expect(credentialFieldsForAuthMode(WebhookAuthModeEnum.Bearer)).toEqual([
      { key: 'BearerToken', label: 'Bearer token' },
    ]);
  });

  it('credentialFieldsForAuthMode() returns Username and Password for Basic', () => {
    expect(credentialFieldsForAuthMode(WebhookAuthModeEnum.Basic)).toEqual([
      { key: 'Username', label: 'Username' },
      { key: 'Password', label: 'Password' },
    ]);
  });

  it('toControlName() converts a PascalCase dictionary key into its camelCase control name', () => {
    expect(toControlName('BearerToken')).toBe('bearerToken');
    expect(toControlName('Username')).toBe('username');
    expect(toControlName('Password')).toBe('password');
  });

  it('buildAuthCredentialsPayload() reads only the fields the selected auth mode actually needs', () => {
    const payload = buildAuthCredentialsPayload(WebhookAuthModeEnum.Basic, {
      bearerToken: 'stray-leftover-value',
      username: 'operator',
      password: 'hunter2',
    });

    expect(payload).toEqual({ Username: 'operator', Password: 'hunter2' });
  });

  it('authCredentialsPayloadIsComplete() is true only when every field is non-blank', () => {
    expect(authCredentialsPayloadIsComplete({ BearerToken: 'value' })).toBeTrue();
    expect(authCredentialsPayloadIsComplete({ BearerToken: '' })).toBeFalse();
    expect(authCredentialsPayloadIsComplete({ Username: 'a', Password: '   ' })).toBeFalse();
  });

  it('summarizeEventTypeNames() joins names with a comma, or reports "(none)"', () => {
    expect(summarizeEventTypeNames(['DnsZoneChanged', 'DnsCacheEvicted'])).toBe(
      'DnsZoneChanged, DnsCacheEvicted',
    );
    expect(summarizeEventTypeNames([])).toBe('(none)');
    expect(summarizeEventTypeNames(null)).toBe('(none)');
    expect(summarizeEventTypeNames(undefined)).toBe('(none)');
  });
});
