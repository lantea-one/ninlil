import { md5 } from './md5.util';

/**
 * This module builds a Gravatar (gravatar.com) avatar image URL for an account's own e-mail
 * address -- a deliberate, confirmed design choice: Gravatar with a graceful local fallback,
 * rather than requiring an operator to upload/host an avatar image of their own. The URL is
 * embedded directly in an `<img>` element's own `src`; the browser fetches it client-side, so
 * this carries no server-side dependency or offline-availability risk -- see
 * {@link buildGravatarInitials} and the Profile page's own `onerror` handler for the fallback this
 * design relies on when the image request 404s (no Gravatar registered for this hash) or fails
 * outright (an offline homelab install with no path to gravatar.com at all).
 */

/** The `d=404` query parameter tells Gravatar's own server to return a real HTTP 404 instead of a generic silhouette placeholder when no avatar is registered for the hash -- exactly what an `onerror` handler needs to distinguish "no Gravatar" from a transient network failure and fall back to a local initials avatar either way. */
const NOT_FOUND_BEHAVIOR = 'd=404';

/**
 * This function builds the Gravatar image URL for `email`, at `sizePixels` (Gravatar's own `s`
 * parameter -- the image is served pre-scaled to this square size). Per Gravatar's own published
 * hashing convention, the address is trimmed and lowercased before hashing, so that the same
 * mailbox produces the same avatar regardless of how it happens to be capitalized or padded
 * wherever it was typed.
 */
export function buildGravatarUrl(email: string, sizePixels: number): string {
  const normalizedEmail = email.trim().toLowerCase();
  const hash = md5(normalizedEmail);

  return `https://www.gravatar.com/avatar/${hash}?s=${sizePixels}&${NOT_FOUND_BEHAVIOR}`;
}

/**
 * This function derives a short (1-2 character) initials string for the local, CSS-only fallback
 * avatar shown once a Gravatar image request fails -- preferring `displayName` (splitting on
 * whitespace and taking the first letter of up to the first two words, e.g. "Jamie Smith" -> "JS")
 * when set, and otherwise falling back to the first letter of `fallbackName` (this project passes
 * the account's own sign-in `userName`, since a fresh account may not have set a display name
 * yet). Returns `"?"` only in the degenerate case where neither carries any usable character.
 */
export function buildGravatarInitials(
  displayName: string | null | undefined,
  fallbackName: string | null | undefined,
): string {
  const trimmedDisplayName = displayName?.trim();
  if (trimmedDisplayName) {
    const words = trimmedDisplayName.split(/\s+/).filter((word) => word.length > 0);
    const initials = words
      .slice(0, 2)
      .map((word) => word.charAt(0).toUpperCase())
      .join('');
    if (initials) return initials;
  }

  const trimmedFallbackName = fallbackName?.trim();
  if (trimmedFallbackName) return trimmedFallbackName.charAt(0).toUpperCase();

  return '?';
}
