import { FormControl } from '@angular/forms';
import {
  assertDhcpv4OptionValueLength,
  assertDhcpv6OptionValueLength,
  dhcpv4OptionHint,
  dhcpv4OptionTagValidator,
  dhcpv6OptionCodeValidator,
} from './dhcp-option-rules.util';

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

describe('dhcpv6OptionCodeValidator', () => {
  it('rejects 0, 256 and every code the engine writes itself', () => {
    expect(dhcpv6OptionCodeValidator(new FormControl(0))?.['min']).toBeDefined();
    expect(dhcpv6OptionCodeValidator(new FormControl(256))?.['max']).toBeDefined();
    for (const code of [1, 2, 3, 4, 5, 6, 8, 9, 11, 13, 14, 18, 19, 20, 25, 26, 37, 39]) {
      expect(dhcpv6OptionCodeValidator(new FormControl(code))?.['reservedTag']).toBeDefined();
    }
  });

  it('accepts Preference (7), DNS servers (23) and 255', () => {
    for (const code of [7, 23, 255]) {
      expect(dhcpv6OptionCodeValidator(new FormControl(code))).toBeNull();
    }
  });
});

describe('assertDhcpv6OptionValueLength', () => {
  it('accepts 65535 bytes and rejects 65536', () => {
    expect(() => assertDhcpv6OptionValueLength('AA'.repeat(65535))).not.toThrow();
    expect(() => assertDhcpv6OptionValueLength('AA'.repeat(65536))).toThrowError(/65536 bytes/);
  });
});

describe('dhcpv4OptionHint', () => {
  it('explains options 66 and 150 and says nothing about others', () => {
    expect(dhcpv4OptionHint(66)).toContain('as text');
    expect(dhcpv4OptionHint(150)).toContain('RFC 5859');
    expect(dhcpv4OptionHint(6)).toBeNull();
  });
});
