import { resolveRecordName } from './record-name';

/** These tests cover `resolveRecordName`, the short-name rule of the record form. */
describe('resolveRecordName', () => {
  const zone = 'example.test';

  it('appends the zone to a short name', () => {
    expect(resolveRecordName('www', zone)).toBe('www.example.test');
  });

  it('appends the zone to a name with several labels that is not in the zone', () => {
    expect(resolveRecordName('a.b', zone)).toBe('a.b.example.test');
  });

  it('keeps a name that is already below the zone', () => {
    expect(resolveRecordName('www.example.test', zone)).toBe('www.example.test');
  });

  it('keeps the zone apex written as @', () => {
    expect(resolveRecordName('@', zone)).toBe('@');
  });

  it('keeps the zone name itself', () => {
    expect(resolveRecordName('example.test', zone)).toBe('example.test');
  });

  it('keeps a name in a different case', () => {
    expect(resolveRecordName('WWW.Example.Test', zone)).toBe('WWW.Example.Test');
  });

  it('appends the zone when the name only ends with the zone text', () => {
    expect(resolveRecordName('wwwexample.test', zone)).toBe('wwwexample.test.example.test');
  });

  it('ignores one trailing dot when comparing', () => {
    expect(resolveRecordName('www.example.test.', zone)).toBe('www.example.test.');
    expect(resolveRecordName('www', 'example.test.')).toBe('www.example.test');
  });

  it('keeps an absolute name outside the zone so the server explains the refusal', () => {
    expect(resolveRecordName('www.other.test.', zone)).toBe('www.other.test.');
  });

  it('returns the typed text when the zone is not loaded or the field is empty', () => {
    expect(resolveRecordName('www', null)).toBe('www');
    expect(resolveRecordName('  ', zone)).toBe('');
  });
});
