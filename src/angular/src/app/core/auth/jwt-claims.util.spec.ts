import { decodeAuthClaims } from './jwt-claims.util';

/**
 * This function builds an unsigned JWT with the given payload. {@link decodeAuthClaims} never
 * checks the signature, so this is enough to decode.
 */
function buildFakeToken(payload: Record<string, unknown>): string {
  const base64UrlEncode = (value: string) =>
    btoa(value).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');

  const header = base64UrlEncode(JSON.stringify({ alg: 'none', typ: 'JWT' }));
  const body = base64UrlEncode(JSON.stringify(payload));
  return `${header}.${body}.`;
}

/**
 * These tests cover `decodeAuthClaims`: single- and multi-valued role and permission claims,
 * missing claims, and malformed tokens. Plain function calls on hand-built unsigned tokens.
 */
describe('decodeAuthClaims', () => {
  it('decodes sub/unique_name plus single-valued role and permission claims', () => {
    const token = buildFakeToken({
      sub: 'b946926d-7b4e-4af0-912b-ed2808361283',
      unique_name: 'oaiadmin',
      'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': 'Administrators',
      'https://ninlil.one/claims/permission': '*',
    });

    const claims = decodeAuthClaims(token);

    expect(claims).not.toBeNull();
    expect(claims?.userId).toBe('b946926d-7b4e-4af0-912b-ed2808361283');
    expect(claims?.userName).toBe('oaiadmin');
    expect(claims?.roles).toEqual(['Administrators']);
    expect(claims?.permissions).toEqual(['*']);
  });

  it('decodes multi-valued role and permission claims carried as JSON arrays', () => {
    const token = buildFakeToken({
      sub: 'user-id',
      unique_name: 'operator',
      'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': ['Operators', 'Auditors'],
      'https://ninlil.one/claims/permission': ['zones:read', 'zones:write'],
    });

    const claims = decodeAuthClaims(token);

    expect(claims?.roles).toEqual(['Operators', 'Auditors']);
    expect(claims?.permissions).toEqual(['zones:read', 'zones:write']);
  });

  it('returns empty arrays, not null fields, when no role or permission claim is present', () => {
    const token = buildFakeToken({ sub: 'user-id', unique_name: 'nobody' });

    const claims = decodeAuthClaims(token);

    expect(claims?.roles).toEqual([]);
    expect(claims?.permissions).toEqual([]);
  });

  it('returns null for a token with fewer than two dot-separated segments', () => {
    expect(decodeAuthClaims('not-a-jwt')).toBeNull();
  });

  it('returns null for a token whose payload segment is not valid base64url JSON', () => {
    expect(decodeAuthClaims('header.not-valid-base64!!!.signature')).toBeNull();
  });
});
