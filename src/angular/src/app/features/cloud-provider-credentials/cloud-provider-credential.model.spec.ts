import { CloudDnsProviderTypeEnum } from '../../api-client';
import {
  buildCredentialsPayload,
  credentialFieldsFor,
  credentialsPayloadIsComplete,
  toControlName,
} from './cloud-provider-credential.model';

describe('cloud-provider-credential.model', () => {
  it('credentialFieldsFor() returns a single ApiToken field for Cloudflare', () => {
    expect(credentialFieldsFor(CloudDnsProviderTypeEnum.Cloudflare)).toEqual([
      { key: 'ApiToken', label: 'API token' },
    ]);
  });

  it('credentialFieldsFor() returns a single ApiToken field for DigitalOcean', () => {
    expect(credentialFieldsFor(CloudDnsProviderTypeEnum.DigitalOcean)).toEqual([
      { key: 'ApiToken', label: 'API token' },
    ]);
  });

  it('credentialFieldsFor() returns both AccessKeyId and SecretAccessKey for Route53', () => {
    expect(credentialFieldsFor(CloudDnsProviderTypeEnum.Route53)).toEqual([
      { key: 'AccessKeyId', label: 'Access key ID' },
      { key: 'SecretAccessKey', label: 'Secret access key' },
    ]);
  });

  it('toControlName() converts a PascalCase dictionary key into its camelCase control name', () => {
    expect(toControlName('ApiToken')).toBe('apiToken');
    expect(toControlName('AccessKeyId')).toBe('accessKeyId');
    expect(toControlName('SecretAccessKey')).toBe('secretAccessKey');
  });

  it('buildCredentialsPayload() reads only the fields the selected provider type actually needs', () => {
    const payload = buildCredentialsPayload(CloudDnsProviderTypeEnum.Route53, {
      apiToken: 'stray-leftover-value',
      accessKeyId: 'AKIAEXAMPLE',
      secretAccessKey: 'super-secret',
    });

    expect(payload).toEqual({ AccessKeyId: 'AKIAEXAMPLE', SecretAccessKey: 'super-secret' });
  });

  it('credentialsPayloadIsComplete() is true only when every field is non-blank', () => {
    expect(credentialsPayloadIsComplete({ ApiToken: 'value' })).toBeTrue();
    expect(credentialsPayloadIsComplete({ ApiToken: '' })).toBeFalse();
    expect(credentialsPayloadIsComplete({ ApiToken: '   ' })).toBeFalse();
    expect(credentialsPayloadIsComplete({ AccessKeyId: 'a', SecretAccessKey: '' })).toBeFalse();
  });
});
