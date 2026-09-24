import { AuthClaims } from './auth.model';

/**
 * This module decodes a JSON Web Token access token's own payload, purely client-side, so the
 * app can read the identity/role/permission claims `Ninlil.Dns.Identity`'s own
 * `UserClaimsBuilder` (see `src/dotnet/src/Ninlil.Dns.Identity/Claims/UserClaimsBuilder.cs`)
 * baked into it -- without verifying the token's own signature.
 *
 * This is deliberately *not* a trust boundary: the real backend re-validates the token's
 * signature and expiry on every authenticated request regardless of what this app reads out of
 * it. Decoding it here only drives client-side navigation/UI decisions (which nav items to show,
 * which routes to block) -- a nav item stays hidden AND its route stays blocked by a guard, never
 * hiding alone -- applied entirely on the client's own, inherently-untrusted side of that
 * boundary.
 */

/**
 * This constant contains the exact claim type literal
 * `Ninlil.Dns.Identity.NinlilDnsClaimTypes.Permission` issues -- verified directly against
 * a real, decoded access token from a real login round-trip (not assumed), since a permission
 * check that silently looked at the wrong claim key would fail open or closed without ever
 * throwing.
 */
const PERMISSION_CLAIM_TYPE = 'https://ninlil.one/claims/permission';

/**
 * This constant contains the exact claim type literal `System.Security.Claims.ClaimTypes.Role`
 * resolves to at runtime -- also verified against a real, decoded token rather than assumed from
 * the constant's own familiar short name.
 */
const ROLE_CLAIM_TYPE = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

/** This function normalizes a JWT claim value that may be one value or an array of values. */
function toStringArray(value: unknown): string[] {
  if (value === null || value === undefined) return [];
  return Array.isArray(value) ? value.map(String) : [String(value)];
}

/**
 * This function decodes a JSON Web Token's own payload segment into an {@link AuthClaims},
 * returning `null` if the token is not well-formed enough to decode at all (never throws --
 * a malformed token should simply be treated as "no usable claims," not crash navigation).
 */
export function decodeAuthClaims(accessToken: string): AuthClaims | null {
  const segments = accessToken.split('.');
  if (segments.length < 2) return null;

  try {
    // A JWT payload segment is base64url, not standard base64 (`-`/`_` instead of `+`/`/`, and
    // its own trailing `=` padding stripped) -- swap the two substituted characters back and
    // re-pad to a multiple of 4 before handing it to the standard `atob`, which only understands
    // ordinary base64.
    const base64Url = segments[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64Url + '='.repeat((4 - (base64Url.length % 4)) % 4);
    // `atob` decodes into a "binary string" (one UTF-16 code unit per decoded byte, not real
    // text), but the payload itself is UTF-8 JSON -- mapping each byte to a `%XX` escape and
    // running it through `decodeURIComponent` re-assembles the original UTF-8 text correctly,
    // rather than mis-decoding any non-ASCII claim value (a non-Latin display name, for example)
    // as if each byte were already its own Latin-1 character.
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
