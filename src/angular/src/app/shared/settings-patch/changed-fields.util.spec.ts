import { changedFields, hasChangedFields } from './changed-fields.util';

/**
 * These tests cover the changed-only helper of the settings pages: scalar fields, lists and nested
 * objects (sent whole when any entry differs) and time span texts (equal when they name the same
 * length of time). Pure functions, no TestBed.
 */
describe('changedFields', () => {
  it('returns nothing when no field changed', () => {
    const body = { enable: true, name: 'a', list: ['x'], nested: { a: 1 } };

    expect(changedFields(body, structuredClone(body))).toEqual({});
  });

  it('returns only the scalar fields that differ', () => {
    const baseline = { enable: false, port: 53, name: 'a' };

    expect(changedFields(baseline, { enable: true, port: 53, name: 'a' })).toEqual({
      enable: true,
    });
  });

  it('sends a list whole when one entry changed', () => {
    const baseline = { domains: ['a.example', 'b.example'], enable: true };

    expect(changedFields(baseline, { domains: ['a.example', 'c.example'], enable: true })).toEqual({
      domains: ['a.example', 'c.example'],
    });
  });

  it('treats a list with the same entries in the same order as unchanged', () => {
    const baseline = { domains: ['a.example', 'b.example'] };

    expect(changedFields(baseline, { domains: ['a.example', 'b.example'] })).toEqual({});
  });

  it('treats a reordered list and a shorter list as changed', () => {
    const baseline = { domains: ['a', 'b'] };

    expect(changedFields(baseline, { domains: ['b', 'a'] })).toEqual({ domains: ['b', 'a'] });
    expect(changedFields(baseline, { domains: ['a'] })).toEqual({ domains: ['a'] });
  });

  it('compares lists of objects by content and sends the whole list', () => {
    const baseline = { rows: [{ name: 'a', addresses: ['1'] }] };
    const current = { rows: [{ name: 'a', addresses: ['1', '2'] }] };

    expect(changedFields(baseline, current)).toEqual({ rows: current.rows });
    expect(changedFields(baseline, structuredClone(baseline))).toEqual({});
  });

  it('sends a nested object whole when one of its fields changed', () => {
    const baseline = { caa: { defaultEnabled: true, issuer: '' } };
    const current = { caa: { defaultEnabled: true, issuer: 'ca.example' } };

    expect(changedFields(baseline, current)).toEqual({ caa: current.caa });
  });

  it('compares time spans by value, not by text', () => {
    const baseline = { interval: '0:05:00', timeout: '1.00:00:00', other: '00:00:03' };

    expect(
      changedFields(baseline, {
        interval: '00:05:00',
        timeout: '24:00:00',
        other: '00:00:04',
      }),
    ).toEqual({ other: '00:00:04' });
  });

  it('does not treat other texts as time spans', () => {
    expect(changedFields({ name: 'abc' }, { name: 'abd' })).toEqual({ name: 'abd' });
    expect(changedFields({ name: '1:2:3' }, { name: '1:2:4' })).toEqual({ name: '1:2:4' });
  });

  it('distinguishes null from an empty list and from a missing baseline field', () => {
    expect(changedFields({ list: [] as string[] | null }, { list: null })).toEqual({ list: null });
    expect(changedFields({} as { later?: number }, { later: 1 })).toEqual({ later: 1 });
  });
});

describe('hasChangedFields', () => {
  it('is false for an empty difference and true otherwise', () => {
    expect(hasChangedFields({})).toBeFalse();
    expect(hasChangedFields({ enable: false })).toBeTrue();
  });
});
