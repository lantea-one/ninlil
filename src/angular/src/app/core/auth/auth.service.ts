import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService as AuthApiService, RefreshTokenDeliveryEnum } from '../../api-client';
import { AuthClaims, LoginOutcome } from './auth.model';
import { decodeAuthClaims } from './jwt-claims.util';

/**
 * This constant contains the exact permission value
 * `Ninlil.Dns.Identity.Authorization.PermissionAuthorizationHandler.WildcardPermission`
 * resolves to on the backend -- a permission claim holding this literal value satisfies every
 * permission check, the same bootstrap-administrator behavior the backend itself implements.
 */
const WILDCARD_PERMISSION = '*';

/**
 * This service owns this app's sign-in session: the access token and the claims decoded out of it,
 * held **only in memory** (plain Angular signals, never `localStorage`/`sessionStorage`), per this
 * project's own deliberate security decision.
 *
 * <p>The refresh token is never held here at all. Every sign-in asks the backend for cookie
 * delivery (`refreshTokenDelivery: Cookie`), so the refresh token lives only in an HttpOnly,
 * `SameSite=Strict` cookie scoped to `/api/auth` -- page script, and therefore an XSS bug, can never
 * read it, yet the browser presents it to the refresh and sign-out endpoints on its own. That is
 * what lets {@link restoreSession} renew the session after a full page reload. "Remember me"
 * decides whether that cookie (and the server-side session behind it) outlives the browser:
 * checked, it lasts the long refresh lifetime; unchecked, it is a browser-session cookie backed by
 * a short, activity-renewed server-side lifetime.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly accessTokenSignal = signal<string | null>(null);
  private readonly claimsSignal = signal<AuthClaims | null>(null);

  /**
   * This field holds an in-flight refresh's own promise, so two requests that both 401 at
   * roughly the same moment (a real, ordinary race under concurrent requests, not a contrived
   * edge case) share one refresh attempt rather than each rotating the refresh cookie out from
   * under the other -- the second caller's own cookie would already have been revoked by the
   * first's successful exchange (rotation, per `AuthController.Refresh`'s own doc comment),
   * making the second call fail even though the session is, in truth, still healthy.
   */
  private refreshInFlight: Promise<boolean> | null = null;

  /**
   * The "Remember me" choice of a sign-in still waiting on its second factor, repeated to the
   * two-factor endpoint so the completed session gets the lifetime the operator actually chose.
   */
  private pendingRememberMe = false;

  /** Whether a completed sign-in currently holds a usable access token. */
  readonly isAuthenticated = computed(() => this.accessTokenSignal() !== null);

  /** The current session's own decoded claims, or `null` when signed out. */
  readonly claims = computed(() => this.claimsSignal());

  /** The current session's own sign-in name, or `null` when signed out. */
  readonly userName = computed(() => this.claimsSignal()?.userName ?? null);

  private readonly authApi = inject(AuthApiService);

  /** The current access token, read by {@link AuthInterceptor}. Never persisted. */
  get accessToken(): string | null {
    return this.accessTokenSignal();
  }

  /**
   * This method checks a username and password against `POST /api/auth/login`, either
   * completing sign-in immediately or reporting that a second factor is still required. The
   * refresh token always comes back as the HttpOnly cookie, never in the response this app sees.
   *
   * @param rememberMe Whether the session should outlive the browser (see this class's doc).
   */
  async login(userName: string, password: string, rememberMe = false): Promise<LoginOutcome> {
    const response = await firstValueFrom(
      this.authApi.apiAuthLoginPost({
        loginRequest: {
          userName,
          password,
          rememberMe,
          refreshTokenDelivery: RefreshTokenDeliveryEnum.Cookie,
        },
      }),
    );

    if (response.requiresTwoFactor) {
      this.pendingRememberMe = rememberMe;
      return { requiresTwoFactor: true, mfaToken: response.mfaToken ?? '' };
    }

    this.applyAccessToken(response.accessToken ?? null);
    return { requiresTwoFactor: false };
  }

  /**
   * This method completes a sign-in {@link login} flagged as requiring a second factor, against
   * `POST /api/auth/login/two-factor`.
   */
  async completeTwoFactorLogin(
    mfaToken: string,
    code: string,
    provider: 'Authenticator' | 'Email' | 'RecoveryCode',
  ): Promise<void> {
    const response = await firstValueFrom(
      this.authApi.apiAuthLoginTwoFactorPost({
        twoFactorLoginRequest: {
          mfaToken,
          code,
          provider,
          rememberMe: this.pendingRememberMe,
          refreshTokenDelivery: RefreshTokenDeliveryEnum.Cookie,
        },
      }),
    );

    this.pendingRememberMe = false;
    this.applyAccessToken(response.accessToken ?? null);
  }

  /**
   * This method exchanges the browser's refresh cookie for a new access token (the backend
   * rotates the cookie in the same response) against `POST /api/auth/refresh`, returning whether
   * it succeeded. Used by {@link AuthInterceptor}'s own silent-refresh-and-retry and by
   * {@link restoreSession}.
   *
   * <p>A 401 is retried exactly once before giving up: when two tabs refresh at the same moment,
   * both present the same cookie, the first rotates it, and the second's request fails with the
   * old one -- by the time it retries, the browser holds the rotated cookie and the retry
   * succeeds. A session that has genuinely ended simply fails twice.
   */
  async refresh(): Promise<boolean> {
    if (this.refreshInFlight) return this.refreshInFlight;

    this.refreshInFlight = (async () => {
      try {
        for (let attempt = 0; attempt < 2; attempt++) {
          try {
            const result = await firstValueFrom(
              this.authApi.apiAuthRefreshPost({ refreshRequest: {} }),
            );
            this.applyAccessToken(result.accessToken ?? null);
            return true;
          } catch (error) {
            // Only an outright rejection is worth one retry; anything else (the server is
            // unreachable, for instance) will not be fixed by asking again immediately.
            if (!(error instanceof HttpErrorResponse && error.status === 401)) break;
          }
        }
        this.clearSession();
        return false;
      } finally {
        this.refreshInFlight = null;
      }
    })();

    return this.refreshInFlight;
  }

  /**
   * This method resumes a session after a full page load, by exchanging the refresh cookie (if
   * the browser still holds one) for a fresh access token. It runs once, as an app initializer,
   * before the first navigation decides whether the visitor is signed in; with no cookie, or a
   * dead one, it quietly leaves the app signed out.
   */
  async restoreSession(): Promise<void> {
    if (this.isAuthenticated()) return;
    await this.refresh();
  }

  /**
   * This method signs out: clears the in-memory session immediately (so the UI reflects
   * signed-out state even if the network call below fails), then best-effort revokes the session
   * against `POST /api/auth/logout`, which also tells the browser to discard the refresh cookie.
   * That endpoint requires no access token -- the cookie itself is sufficient authorization to
   * revoke the session it belongs to, mirroring `AuthController.Logout`'s own reasoning.
   */
  async logout(): Promise<void> {
    this.clearSession();

    try {
      await firstValueFrom(this.authApi.apiAuthLogoutPost({ refreshRequest: {} }));
    } catch {
      // Best-effort only -- the session is already cleared locally regardless.
    }
  }

  /**
   * This method completes a token-based password reset against `POST /api/auth/reset-password`
   * (`AuthController.ResetPassword`) -- the shared confirming action every "set new password"
   * landing page submits to, reused unchanged by `SetNewPasswordComponent` for both an
   * administrator-driven `EmailLink` reset e-mail and a genuinely self-service request raised via
   * {@link forgotPassword} below. This action is `[AllowAnonymous]` on the backend and
   * touches no session state here either -- neither caller is signed in at this point, and a
   * completed reset does not itself sign the caller in (they still submit their new password
   * through the ordinary {@link login} flow afterward).
   */
  async resetPassword(userId: string, token: string, newPassword: string): Promise<void> {
    await firstValueFrom(
      this.authApi.apiAuthResetPasswordPost({
        confirmPasswordResetRequest: { userId, token, newPassword },
      }),
    );
  }

  /**
   * This method submits a genuinely self-service Forgot Password request against
   * `POST /api/auth/forgot-password` (`AuthController.ForgotPassword`), given only an e-mail
   * address -- no sign-in of any kind. The backend's own response is deliberately identical
   * whether or not the address actually has an account (see that action's own doc comment for
   * why this is a security property, not an oversight) -- this method simply returns that one
   * message string as-is, and {@link ForgotPasswordComponent} shows it unconditionally, so this
   * app never introduces a distinction the backend itself refuses to make.
   */
  async forgotPassword(email: string): Promise<string> {
    const result = await firstValueFrom(
      this.authApi.apiAuthForgotPasswordPost({ forgotPasswordRequest: { email } }),
    );
    return result.message ?? '';
  }

  /**
   * This method applies the access token minted by a completed OpenID Connect sign-in, exactly
   * the same way {@link login}/{@link completeTwoFactorLogin}/{@link refresh} apply a local
   * sign-in's own -- the one, shared token-store path every completed sign-in in this app goes
   * through. {@link SsoCallbackComponent} calls this after parsing the access token out of the URL
   * fragment `OpenIdConnectOptions.Events.OnTokenValidated` redirects the browser to; that same
   * response already set the refresh cookie, so no refresh token ever passes through the URL.
   */
  applySsoAccessToken(accessToken: string): void {
    this.applyAccessToken(accessToken);
  }

  /**
   * This method checks whether the current session holds a given permission, including the
   * wildcard-permission shortcut the bootstrap administrator's own role carries.
   */
  hasPermission(permission: string): boolean {
    const claims = this.claimsSignal();
    if (!claims) return false;
    return (
      claims.permissions.includes(WILDCARD_PERMISSION) || claims.permissions.includes(permission)
    );
  }

  /**
   * This method checks whether the current session holds at least one of several permissions --
   * for a page, such as the settings area, that is useful to anyone who can read any one of its
   * sub-pages.
   */
  hasAnyPermission(permissions: readonly string[]): boolean {
    return permissions.some((permission) => this.hasPermission(permission));
  }

  /** This method applies a fresh access token and decodes the new claims. */
  private applyAccessToken(accessToken: string | null): void {
    this.accessTokenSignal.set(accessToken);
    this.claimsSignal.set(accessToken ? decodeAuthClaims(accessToken) : null);
  }

  /** This method clears every piece of in-memory session state. */
  private clearSession(): void {
    this.accessTokenSignal.set(null);
    this.claimsSignal.set(null);
  }
}
