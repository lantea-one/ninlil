import { buildClientSecretPatchField, joinScopes, splitScopes } from './sso-settings.model';

/**
 * These tests cover the SSO form helpers: splitScopes, joinScopes and
 * buildClientSecretPatchField. Plain unit tests.
 */
describe('splitScopes', () => {
  it('splits on whitespace', () => {
    expect(splitScopes('openid profile email')).toEqual(['openid', 'profile', 'email']);
  });

  it('splits on commas', () => {
    expect(splitScopes('openid, profile, email')).toEqual(['openid', 'profile', 'email']);
  });

  it('splits on a mix of commas and whitespace', () => {
    expect(splitScopes('openid,  profile\n email,groups')).toEqual([
      'openid',
      'profile',
      'email',
      'groups',
    ]);
  });

  it('drops empty entries from repeated separators', () => {
    expect(splitScopes('openid,,  profile')).toEqual(['openid', 'profile']);
  });

  it('drops duplicate entries, keeping the first occurrence', () => {
    expect(splitScopes('openid profile openid email')).toEqual(['openid', 'profile', 'email']);
  });

  it('returns an empty array for a blank string', () => {
    expect(splitScopes('   ')).toEqual([]);
  });
});

describe('joinScopes', () => {
  it('joins with a single space', () => {
    expect(joinScopes(['openid', 'profile', 'email'])).toBe('openid profile email');
  });

  it('returns an empty string for null', () => {
    expect(joinScopes(null)).toBe('');
  });

  it('returns an empty string for undefined', () => {
    expect(joinScopes(undefined)).toBe('');
  });

  it('round-trips through splitScopes', () => {
    const original = ['openid', 'profile', 'email', 'groups'];
    expect(splitScopes(joinScopes(original))).toEqual(original);
  });
});

describe('buildClientSecretPatchField', () => {
  it('includes clientSecret when non-blank', () => {
    expect(buildClientSecretPatchField('a-real-secret')).toEqual({
      clientSecret: 'a-real-secret',
    });
  });

  it('omits clientSecret entirely when blank', () => {
    expect(buildClientSecretPatchField('')).toEqual({});
  });

  it('omits clientSecret entirely when whitespace-only', () => {
    expect(buildClientSecretPatchField('   ')).toEqual({});
  });
});
