import { buildSharedSecretPatchField, joinLines, splitLines } from './reverse-proxy-settings.model';

/**
 * These tests cover the line splitting and joining helpers and buildSharedSecretPatchField. Plain
 * unit tests.
 */
describe('reverse-proxy-settings.model', () => {
  describe('splitLines', () => {
    it('splits a one-entry-per-line textarea value into a trimmed array', () => {
      expect(splitLines('203.0.113.10\n  10.0.0.0/24  \n')).toEqual([
        '203.0.113.10',
        '10.0.0.0/24',
      ]);
    });

    it('drops blank lines entirely', () => {
      expect(splitLines('203.0.113.10\n\n\n10.0.0.0/24')).toEqual(['203.0.113.10', '10.0.0.0/24']);
    });

    it('returns an empty array for an empty or whitespace-only value', () => {
      expect(splitLines('')).toEqual([]);
      expect(splitLines('   \n  \n')).toEqual([]);
    });
  });

  describe('joinLines', () => {
    it('joins an array back into one entry per line', () => {
      expect(joinLines(['203.0.113.10', '10.0.0.0/24'])).toBe('203.0.113.10\n10.0.0.0/24');
    });

    it('returns an empty string for null/undefined/an empty array', () => {
      expect(joinLines(null)).toBe('');
      expect(joinLines(undefined)).toBe('');
      expect(joinLines([])).toBe('');
    });
  });

  describe('buildSharedSecretPatchField', () => {
    it('omits the sharedSecret key entirely when the field is blank', () => {
      const result = buildSharedSecretPatchField('');

      expect(result).toEqual({});
      expect('sharedSecret' in result).toBeFalse();
    });

    it('omits the sharedSecret key entirely when the field is only whitespace', () => {
      const result = buildSharedSecretPatchField('   ');

      expect('sharedSecret' in result).toBeFalse();
    });

    it('includes the sharedSecret key when the caller actually typed a new value', () => {
      const result = buildSharedSecretPatchField('correct-horse-battery-staple');

      expect(result).toEqual({ sharedSecret: 'correct-horse-battery-staple' });
    });
  });
});
