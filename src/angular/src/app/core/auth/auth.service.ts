import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService as AuthApiService, RefreshTokenDeliveryEnum } from '../../api-client';
import { AuthClaims, LoginOutcome } from './auth.model';
import { decodeAuthClaims } from './jwt-claims.util';

/**
 * This constant matches the backend's `PermissionAuthorizationHandler.WildcardPermission`. A
 * permission claim with this value satisfies every permission check, on the backend and here.
 */
const WILDCARD_PERMISSION = '*';

/**
 * This service owns the sign-in session: the access token and the claims decoded from it. Both
 * live **only in memory** (Angular signals, never `localStorage` or `sessionStorage`), so script
 * injected by an XSS bug cannot find a stored token. The login, two-factor, SSO callback and
 * forgot/reset password pages call it; `authInterceptor` reads the token; the guards,
 * `IfPermissionDirective`, `HasPermissionPipe` and the nav call {@link hasPermission}.
 *
 * <p>The refresh token is never held here. Every sign-in asks for cookie delivery
 * (`refreshTokenDelivery: Cookie`), so the refresh token lives only in an HttpOnly,
 * `SameSite=Strict` cookie scoped to `/api/auth`. Page script cannot read it, but the browser
 * sends it to the refresh and logout endpoints, which is how {@link restoreSession} renews the
 * session after a page reload. "Remember me" decides whether that cookie outlives the browser:
 * checked, it lasts the long refresh lifetime; unchecked, it is a browser-session cookie backed by
 * a short server-side lifetime that activity renews.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly accessTokenSignal = signal<string | null>(null);
  private readonly claimsSignal = signal<AuthClaims | null>(null);

  /**
   * This field holds the promise of a refresh in flight, so concurrent requests that all get a
   * 401 share one refresh. Each refresh rotates the cookie and revokes the old one, so a second,
   * parallel refresh would fail and end a healthy session.
   */
  private refreshInFlight: Promise<boolean> | null = null;

  /**
   * The "Remember me" choice of a sign-in still waiting on its second factor, repeated to the
   * two-factor endpoint so the completed session gets the lifetime the operator chose.
   */
  private pendingRememberMe = false;

  /**
   * The message {@link endSessionAfterSecurityChange} left for the login page, until
   * {@link takeSignInNotice} reads it.
   */
  private signInNotice: string | null = null;

  /** Whether a completed sign-in currently holds a usable access token. */
  readonly isAuthenticated = computed(() => this.accessTokenSignal() !== null);

  /** The current session's decoded claims, or `null` when signed out. */
  readonly claims = computed(() => this.claimsSignal());

  /** The current session's sign-in name, or `null` when signed out. */
  readonly userName = computed(() => this.claimsSignal()?.userName ?? null);

  private readonly authApi = inject(AuthApiService);

  /** The current access token, read by `authInterceptor`. Never persisted. */
  get accessToken(): string | null {
    return this.accessTokenSignal();
  }

  /**
   * This method checks a username and password against `POST /api/auth/login`, either
   * completing sign-in immediately or reporting that a second factor is still required. The
   * refresh token always comes back as the HttpOnly cookie, never in the response body.
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
   * it succeeded. `authInterceptor` calls it to retry a request after a 401, and
   * {@link restoreSession} calls it at startup. On failure it clears the session.
   *
   * <p>A 401 is retried once. When two tabs refresh at the same moment, both send the same
   * cookie, the first rotates it and the second fails with the old one. By the time the second
   * retries, the browser holds the rotated cookie and the retry succeeds. A session that has ended
   * fails twice.
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
   * That endpoint needs no access token: holding the cookie is enough to revoke its session.
   */
  async logout(): Promise<void> {
    this.clearSession();

    try {
      await firstValueFrom(this.authApi.apiAuthLogoutPost({ refreshRequest: {} }));
    } catch {
      // Best effort: the session is already cleared locally.
    }
  }

  /**
   * This method ends the session after the signed-in user changed their own password or
   * two-factor settings, and leaves a message for the login page to show. The server ends every
   * session of the account on such a change, this one included: the access token answers 401 and
   * the refresh cookie is revoked. Clearing the session here, instead of letting the next request
   * fail, means the user sees why they are asked to sign in again. The logout call is best effort,
   * as in {@link logout}.
   *
   * @param notice What changed and what to do next, for example "Your password was changed.".
   */
  async endSessionAfterSecurityChange(notice: string): Promise<void> {
    await this.logout();
    this.signInNotice = notice;
  }

  /**
   * This method returns the message {@link endSessionAfterSecurityChange} left for the login page
   * and forgets it, so the page shows it once. It returns `null` when there is none.
   */
  takeSignInNotice(): string | null {
    const notice = this.signInNotice;
    this.signInNotice = null;
    return notice;
  }

  /**
   * This method completes a token-based password reset against the anonymous
   * `POST /api/auth/reset-password`. The `/reset-password` page calls it for both kinds of reset
   * link: one an administrator sends from the Users page and one requested through
   * {@link forgotPassword}. It does not touch the session; after a reset the user signs in
   * through {@link login} as usual.
   */
  async resetPassword(userId: string, token: string, newPassword: string): Promise<void> {
    await firstValueFrom(
      this.authApi.apiAuthResetPasswordPost({
        confirmPasswordResetRequest: { userId, token, newPassword },
      }),
    );
  }

  /**
   * This method submits a Forgot Password request for an e-mail address against the anonymous
   * `POST /api/auth/forgot-password`. The backend returns the same message whether or not the
   * address has an account, so the response cannot be used to discover accounts. This method
   * returns that message unchanged and `ForgotPasswordComponent` always shows it.
   */
  async forgotPassword(email: string): Promise<string> {
    const result = await firstValueFrom(
      this.authApi.apiAuthForgotPasswordPost({ forgotPasswordRequest: { email } }),
    );
    return result.message ?? '';
  }

  /**
   * This method stores the access token from a completed OpenID Connect sign-in, the same way
   * {@link login} and {@link refresh} store theirs. `SsoCallbackComponent` calls it after reading
   * the token from the URL fragment the backend redirects to. That redirect already set the
   * refresh cookie, so no refresh token ever appears in the URL.
   */
  applySsoAccessToken(accessToken: string): void {
    this.applyAccessToken(accessToken);
  }

  /**
   * This method checks whether the current session holds a permission, counting the `"*"`
   * wildcard as every permission. It only drives UI; the backend enforces every permission.
   */
  hasPermission(permission: string): boolean {
    const claims = this.claimsSignal();
    if (!claims) return false;
    return (
      claims.permissions.includes(WILDCARD_PERMISSION) || claims.permissions.includes(permission)
    );
  }

  /**
   * This method checks whether the current session holds at least one of several permissions,
   * for a page such as Settings that is useful to anyone who can read any one of its sub-pages.
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
