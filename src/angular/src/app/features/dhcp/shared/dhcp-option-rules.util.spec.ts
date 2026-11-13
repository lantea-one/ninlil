import { FormControl } from '@angular/forms';
import {
  assertDhcpv4OptionValueLength,
  assertDhcpv6OptionValueLength,
  dhcpv4OptionHint,
  dhcpv4OptionTagValidator,
  dhcpv6OptionCodeValidator,
  optionTagErrorMessage,
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

describe('optionTagErrorMessage', () => {
  it('returns null when the control has no errors', () => {
    expect(optionTagErrorMessage(null, false)).toBeNull();
  });

  it('gives the DHCPv4 range message for a tag out of range', () => {
    expect(optionTagErrorMessage(dhcpv4OptionTagValidator(new FormControl(0)), false)).toBe(
      'A DHCPv4 option tag is between 1 and 254 (0 is Pad and 255 is End).',
    );
  });

  it('gives the DHCPv6 range message for a code out of range', () => {
    expect(optionTagErrorMessage(dhcpv6OptionCodeValidator(new FormControl(256)), true)).toBe(
      'A DHCPv6 option code is between 1 and 255.',
    );
  });

  it('names the reserved DHCPv6 code with its IANA name', () => {
    expect(optionTagErrorMessage(dhcpv6OptionCodeValidator(new FormControl(2)), true)).toBe(
      'Code 2 (Server Identifier) is written by the server and cannot be set here.',
    );
  });

  it('names the reserved DHCPv4 tag as set by the server or relay', () => {
    expect(optionTagErrorMessage(dhcpv4OptionTagValidator(new FormControl(82)), false)).toBe(
      'Tag 82 is set by the server or the relay agent and cannot be set here.',
    );
  });

  it('gives no message for an empty tag, which required reports on save', () => {
    expect(
      optionTagErrorMessage(dhcpv4OptionTagValidator(new FormControl(null)), false),
    ).toBeNull();
  });
});
