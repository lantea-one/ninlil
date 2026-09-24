import {
  buildPfxPasswordPatchField,
  joinLines,
  splitLines,
  splitListenAddresses,
} from './web-listen-settings.model';

describe('web-listen-settings.model', () => {
  it('splitLines trims and drops empty entries', () => {
    expect(splitLines('  localhost  \n\ndns.example.invalid\n')).toEqual([
      'localhost',
      'dns.example.invalid',
    ]);
  });

  it('joinLines is the inverse of splitLines for a populated array', () => {
    expect(joinLines(['localhost', 'dns.example.invalid'])).toBe('localhost\ndns.example.invalid');
  });

  it('joinLines returns an empty string for null/undefined', () => {
    expect(joinLines(null)).toBe('');
    expect(joinLines(undefined)).toBe('');
  });

  it('buildPfxPasswordPatchField omits the field entirely when blank', () => {
    expect(buildPfxPasswordPatchField('   ')).toEqual({});
  });

  it('buildPfxPasswordPatchField includes the field when non-blank', () => {
    expect(buildPfxPasswordPatchField('a-real-password')).toEqual({
      pfxPassword: 'a-real-password',
    });
  });
  it('splitListenAddresses accepts new lines, commas and spaces, and empty means every address', () => {
    expect(splitListenAddresses('192.168.1.10, ::1\n127.0.0.1')).toEqual([
      '192.168.1.10',
      '::1',
      '127.0.0.1',
    ]);
    expect(splitListenAddresses('')).toEqual([]);
  });
});
