import { md5 } from './md5.util';

/**
 * This module builds Gravatar (gravatar.com) avatar URLs from an e-mail address, so users need not
 * upload an avatar. The browser loads the URL in an `<img>`; the server never contacts Gravatar.
 * When the image fails (no Gravatar for the address, or no internet access), the Profile page's
 * `(error)` handler shows initials from {@link buildGravatarInitials} instead.
 */

/** `d=404` makes Gravatar return a 404 instead of a placeholder silhouette when no avatar exists, so the image's error handler fires and the initials fallback shows. */
const NOT_FOUND_BEHAVIOR = 'd=404';

/**
 * This function builds the Gravatar image URL for `email`, as a square of `sizePixels` (the `s`
 * parameter). As Gravatar specifies, the address is trimmed and lowercased before hashing, so
 * case and spacing do not change the avatar.
 */
export function buildGravatarUrl(email: string, sizePixels: number): string {
  const normalizedEmail = email.trim().toLowerCase();
  const hash = md5(normalizedEmail);

  return `https://www.gravatar.com/avatar/${hash}?s=${sizePixels}&${NOT_FOUND_BEHAVIOR}`;
}

/**
 * This function returns one or two initials for the fallback avatar shown when the Gravatar
 * image fails. It uses the first letters of the first two words of `displayName` ("Jamie Smith"
 * gives "JS"), else the first letter of `fallbackName` (the Profile page passes the sign-in
 * name), else `"?"`.
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
