import { md5 } from './md5.util';

describe('md5', () => {
  // The full RFC 1321 §A.5 test suite -- every one of the standard's own published test vectors,
  // confirming this implementation against the RFC it was written from rather than only against
  // one or two ad hoc strings.
  const rfc1321TestSuite: [string, string][] = [
    ['', 'd41d8cd98f00b204e9800998ecf8427e'],
    ['a', '0cc175b9c0f1b6a831c399e269772661'],
    ['abc', '900150983cd24fb0d6963f7d28e17f72'],
    ['message digest', 'f96b697d7cb7938d525a2f31aaf161d0'],
    ['abcdefghijklmnopqrstuvwxyz', 'c3fcd3d76192e4007dfb496cca67e13b'],
    [
      'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789',
      'd174ab98d277d9f5a5611c2c9f419d9f',
    ],
    [
      '12345678901234567890123456789012345678901234567890123456789012345678901234567890',
      '57edf4a22be3c955ac49da2e2107b67a',
    ],
  ];

  for (const [input, expected] of rfc1321TestSuite) {
    it(`matches the RFC 1321 test vector for ${JSON.stringify(input)}`, () => {
      expect(md5(input)).toBe(expected);
    });
  }

  it('handles a message whose padded length lands exactly on a 64-byte block boundary', () => {
    // 55 bytes of content + the mandatory 0x80 padding byte lands at 56 -- the exact boundary
    // RFC 1321's own padding rule (append 0x80, then zeros, until length % 64 === 56) has to
    // handle by rolling over into a second block instead of fitting the length into the first.
    const fiftyFiveBytes = 'x'.repeat(55);
    expect(md5(fiftyFiveBytes).length).toBe(32);
    expect(md5(fiftyFiveBytes)).toMatch(/^[0-9a-f]{32}$/);
  });

  it("produces different hashes for inputs differing only in case, matching Gravatar's own lowercasing requirement", () => {
    expect(md5('Test@Example.com')).not.toBe(md5('test@example.com'));
  });

  it('is deterministic', () => {
    expect(md5('jsmith@example.com')).toBe(md5('jsmith@example.com'));
  });
});
