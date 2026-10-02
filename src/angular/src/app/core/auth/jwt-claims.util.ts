import { AuthClaims } from './auth.model';

/**
 * This module decodes the payload of an access token in the browser, so the app can read the
 * identity, role and permission claims that the backend's `UserClaimsBuilder`
 * (`Ninlil.Dns.Identity/Claims/UserClaimsBuilder.cs`) puts in it. It does not verify the
 * signature.
 *
 * This is not a trust boundary. The backend validates the token's signature and expiry on every
 * request. The decoded claims only decide what the UI shows: which nav items appear and which
 * routes the guards let through. A permission-gated page is both hidden from the nav and blocked
 * by a guard, never only hidden.
 */

/**
 * This constant is the claim type of `NinlilDnsClaimTypes.Permission` on the backend. It must
 * match: reading the wrong key fails silently, and every permission check would then deny.
 */
const PERMISSION_CLAIM_TYPE = 'https://ninlil.one/claims/permission';

/**
 * This constant is the value of .NET's `ClaimTypes.Role`, which is the long URI below, not the
 * short name `role`.
 */
const ROLE_CLAIM_TYPE = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

/** This function normalizes a JWT claim value that may be one value or an array of values. */
function toStringArray(value: unknown): string[] {
  if (value === null || value === undefined) return [];
  return Array.isArray(value) ? value.map(String) : [String(value)];
}

/**
 * This function decodes a JSON Web Token's payload segment into an {@link AuthClaims}. It
 * returns `null` for a malformed token instead of throwing, so a bad token means "no claims"
 * rather than a crash during navigation.
 */
export function decodeAuthClaims(accessToken: string): AuthClaims | null {
  const segments = accessToken.split('.');
  if (segments.length < 2) return null;

  try {
    // A JWT segment is base64url (`-` and `_` instead of `+` and `/`, padding stripped). `atob`
    // only reads standard base64, so swap the characters back and re-pad to a multiple of 4.
    const base64Url = segments[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64Url + '='.repeat((4 - (base64Url.length % 4)) % 4);
    // `atob` returns one character per byte, but the payload is UTF-8 JSON. Turning each byte
    // into a `%XX` escape and passing it through `decodeURIComponent` decodes the UTF-8, so a
    // non-ASCII claim value (a non-Latin user name, say) is not misread as Latin-1.
    const json = decodeURIComponent(
      atob(padded)
        .split('')
        .map((character) => '%' + character.charCodeAt(0).toString(16).padStart(2, '0'))
        .join(''),
    );
    const payload: Record<string, unknown> = JSON.parse(json);

    return {
      userId: typeof payload['sub'] === 'string' ? payload['sub'] : null,
      userName: typeof payload['unique_name'] === 'string' ? payload['unique_name'] : null,
      roles: toStringArray(payload[ROLE_CLAIM_TYPE]),
      permissions: toStringArray(payload[PERMISSION_CLAIM_TYPE]),
    };
  } catch {
    return null;
  }
}
