import { buildGravatarInitials, buildGravatarUrl } from './gravatar.util';
import { md5 } from './md5.util';

/** These tests cover the Gravatar URL: the hash of the normalized address, `s` and `d=404`. */
describe('buildGravatarUrl', () => {
  it('builds a gravatar.com avatar URL keyed by the MD5 hash of the lowercased, trimmed e-mail address', () => {
    const url = buildGravatarUrl('  JSmith@Example.com  ', 128);

    expect(url).toBe(`https://www.gravatar.com/avatar/${md5('jsmith@example.com')}?s=128&d=404`);
  });

  it('always requests d=404, so a caller can distinguish "no Gravatar" from a generic silhouette', () => {
    const url = buildGravatarUrl('someone@example.com', 96);

    expect(url).toContain('d=404');
  });

  it('is stable regardless of the requested size other than the s= parameter itself', () => {
    const smallUrl = buildGravatarUrl('someone@example.com', 32);
    const largeUrl = buildGravatarUrl('someone@example.com', 256);

    expect(smallUrl.replace('s=32', 's=256')).toBe(largeUrl);
  });
});

/** These tests cover the initials for the fallback avatar, from a display name or a user name. */
describe('buildGravatarInitials', () => {
  it('takes the first letter of up to the first two words of a display name', () => {
    expect(buildGravatarInitials('Jamie Smith', 'jsmith')).toBe('JS');
  });

  it('uppercases the initials', () => {
    expect(buildGravatarInitials('jamie smith', 'jsmith')).toBe('JS');
  });

  it('uses only the first word when the display name is a single word', () => {
    expect(buildGravatarInitials('Jamie', 'jsmith')).toBe('J');
  });

  it('ignores extra whitespace between words', () => {
    expect(buildGravatarInitials('Jamie   Smith', 'jsmith')).toBe('JS');
  });

  it('falls back to the sign-in user name initial when no display name is set', () => {
    expect(buildGravatarInitials(null, 'jsmith')).toBe('J');
    expect(buildGravatarInitials(undefined, 'jsmith')).toBe('J');
    expect(buildGravatarInitials('', 'jsmith')).toBe('J');
    expect(buildGravatarInitials('   ', 'jsmith')).toBe('J');
  });

  it('returns "?" when neither a display name nor a fallback name is usable', () => {
    expect(buildGravatarInitials(null, null)).toBe('?');
    expect(buildGravatarInitials('', '')).toBe('?');
  });
});
