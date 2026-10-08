import { FormControl } from '@angular/forms';
import { assertDhcpv4OptionValueLength, dhcpv4OptionTagValidator } from './dhcp-option-rules.util';

describe('dhcpv4OptionTagValidator', () => {
  it('rejects tag 0 and 255 and the server-owned tags', () => {
    expect(dhcpv4OptionTagValidator(new FormControl(0))?.['min']).toBeDefined();
    expect(dhcpv4OptionTagValidator(new FormControl(255))?.['max']).toBeDefined();
    for (const tag of [52, 53, 82]) {
      expect(dhcpv4OptionTagValidator(new FormControl(tag))?.['reservedTag']).toBeDefined();
    }
  });

  it('accepts 1, 66 and 254', () => {
    for (const tag of [1, 66, 254]) {
      expect(dhcpv4OptionTagValidator(new FormControl(tag))).toBeNull();
    }
  });
});

describe('assertDhcpv4OptionValueLength', () => {
  it('accepts 255 bytes and rejects 256', () => {
    expect(() => assertDhcpv4OptionValueLength('AA'.repeat(255))).not.toThrow();
    expect(() => assertDhcpv4OptionValueLength('AA'.repeat(256))).toThrowError(/256 bytes/);
  });
});
