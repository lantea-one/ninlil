import { FormControl } from '@angular/forms';
import {
  CLIENT_IDENTIFIER_MAXIMUM_BYTES,
  clientIdentifierValidator,
  isClientIdentifier,
} from './client-identifier.validator';

/**
 * These tests cover the client identifier validator. The accepted and refused cases mirror
 * `ClientIdentifierFormatTests` in `Ninlil.Dns.Common.Tests`, so the form and the server agree.
 */
describe('client identifier validator', () => {
  const accepted = [
    'AA:BB:CC:DD:EE:FF',
    'aa-bb-cc-dd-ee-ff',
    'aabb.ccdd.eeff',
    'AABBCCDDEEFF',
    '0:1b:63:84:45:e6',
    ' 01:aa:bb:cc:dd:ee:ff ',
    '00:01:00:01:2C:8B:9C:3D:AA:BB:CC:DD:EE:01',
    'aa',
  ];

  const refused = [
    'aa:bb-cc',
    'aabbc',
    '0xaabb',
    'aa::bb',
    'aabb.ccd',
    'declined:10.0.0.5',
    'aa:bb:cc:',
    'aaa:bb',
    'zz:bb',
    'aa bb',
  ];

  for (const value of accepted) {
    it(`isClientIdentifier accepts '${value}'`, () => {
      expect(isClientIdentifier(value)).toBeTrue();
    });
  }

  for (const value of refused) {
    it(`isClientIdentifier refuses '${value}'`, () => {
      expect(isClientIdentifier(value)).toBeFalse();
    });
  }

  it('isClientIdentifier refuses null, undefined, empty and blank input', () => {
    expect(isClientIdentifier(null)).toBeFalse();
    expect(isClientIdentifier(undefined)).toBeFalse();
    expect(isClientIdentifier('')).toBeFalse();
    expect(isClientIdentifier('   ')).toBeFalse();
  });

  it('accepts 85 bytes and refuses 86 bytes', () => {
    const eightyFive = Array(CLIENT_IDENTIFIER_MAXIMUM_BYTES).fill('ab').join(':');
    const eightySix = Array(CLIENT_IDENTIFIER_MAXIMUM_BYTES + 1)
      .fill('ab')
      .join(':');

    expect(isClientIdentifier(eightyFive)).toBeTrue();
    expect(isClientIdentifier(eightySix)).toBeFalse();
    expect(isClientIdentifier('ab'.repeat(86))).toBeFalse();
  });

  it('the validator sets the clientIdentifier error for a refused value', () => {
    const validator = clientIdentifierValidator();

    expect(validator(new FormControl('zz:bb'))).toEqual({ clientIdentifier: true });
    expect(validator(new FormControl('aa-bb'))).toBeNull();
  });

  it('the validator leaves an empty value to Validators.required', () => {
    const validator = clientIdentifierValidator();

    expect(validator(new FormControl(''))).toBeNull();
    expect(validator(new FormControl(null))).toBeNull();
  });
});
