/**
 * This file holds the shapes the app's auth layer passes around internally. They are not wire
 * types; those (`LoginRequest`, `LoginResponse`, ...) are generated under `../../api-client/model`.
 */

/**
 * This interface carries the claims the navigation and route guards need, decoded from the
 * access token by {@link decodeAuthClaims} (`jwt-claims.util.ts`).
 */
export interface AuthClaims {
  /** The signed-in user's id (the JWT's `sub` claim). */
  userId: string | null;

  /** The signed-in user's sign-in name (the JWT's `unique_name` claim). */
  userName: string | null;

  /** Every role this user holds. */
  roles: string[];

  /**
   * Every permission the user holds, from roles and direct claims. A `"*"` entry
   * (`PermissionAuthorizationHandler.WildcardPermission` on the backend) satisfies every
   * permission check, as it does on the backend for the bootstrap administrator.
   */
  permissions: string[];
}

/**
 * This type carries the outcome of a login attempt: either sign-in is already complete, or a
 * second factor is still required and the caller must collect a code and call
 * {@link AuthService.completeTwoFactorLogin}.
 */
export type LoginOutcome =
  { requiresTwoFactor: false } | { requiresTwoFactor: true; mfaToken: string };
