/**
 * This file groups the small set of data shapes this app's own auth layer passes around
 * internally -- mirroring how the backend's own `Ninlil.Web/Models/AuthModels.cs` groups
 * `LoginRequest`/`LoginResponse`/`TwoFactorLoginRequest`/etc. together in one file per feature
 * area, rather than one file per individual type. These are this app's own internal shapes, not
 * wire types -- the wire types themselves (`LoginRequest`, `LoginResponse`, ...) already exist,
 * generated, under `../../api-client/model`.
 */

/**
 * This interface carries the subset of a signed-in user's own claims this app's navigation and
 * route guards need, decoded out of the access token by {@link decodeAuthClaims}
 * (`jwt-claims.util.ts`).
 */
export interface AuthClaims {
  /** The signed-in user's own id (the JWT's `sub` claim). */
  userId: string | null;

  /** The signed-in user's own sign-in name (the JWT's `unique_name` claim). */
  userName: string | null;

  /** Every role this user holds. */
  roles: string[];

  /**
   * Every permission this user's roles and direct claims grant, effectively. A single literal
   * `"*"` entry (`PermissionAuthorizationHandler.WildcardPermission` on the backend) satisfies
   * every permission check, mirroring the backend's own bootstrap-administrator behavior.
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
