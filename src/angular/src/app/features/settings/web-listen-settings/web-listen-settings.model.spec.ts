import { buildPfxPasswordPatchField, joinLines, splitLines } from './web-listen-settings.model';

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
});
