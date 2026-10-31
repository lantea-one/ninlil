import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { AuthService as AuthApiService } from '../../api-client';
import { AuthService } from './auth.service';

/**
 * This function builds an unsigned access token carrying the given permissions, in the shape
 * {@link decodeAuthClaims} reads.
 */
function buildAccessToken(permissions: string[]): string {
  const base64UrlEncode = (value: string) =>
    btoa(value).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');

  const payload = {
    sub: 'user-id',
    unique_name: 'someone',
    'https://ninlil.one/claims/permission': permissions,
  };
  return `${base64UrlEncode('{}')}.${base64UrlEncode(JSON.stringify(payload))}.`;
}

/**
 * This type describes the hand-written fake for the generated `AuthApiService`. It uses plain
 * spies instead of `jasmine.SpyObj<AuthApiService>` because the generated methods are overloaded
 * per `observe` value, and `SpyObj` picks the widest overload, which rejects a plain `of({ ... })`
 * body with an `HttpEvent<T>` type error.
 */
interface FakeAuthApiService {
  apiAuthLoginPost: jasmine.Spy;
  apiAuthLoginTwoFactorPost: jasmine.Spy;
  apiAuthRefreshPost: jasmine.Spy;
  apiAuthLogoutPost: jasmine.Spy;
}

/**
 * These tests cover `AuthService`: sign-in with and without a second factor, the "Remember me"
 * choice, permission checks including the wildcard, cookie refresh (retry, dedupe, failure),
 * session restore, logout and SSO sign-in. TestBed with a spy-backed generated `AuthApiService`.
 */
describe('AuthService', () => {
  let service: AuthService;
  let authApiSpy: FakeAuthApiService;

  beforeEach(() => {
    authApiSpy = {
      apiAuthLoginPost: jasmine.createSpy('apiAuthLoginPost'),
      apiAuthLoginTwoFactorPost: jasmine.createSpy('apiAuthLoginTwoFactorPost'),
      apiAuthRefreshPost: jasmine.createSpy('apiAuthRefreshPost'),
      apiAuthLogoutPost: jasmine.createSpy('apiAuthLogoutPost'),
    };

    TestBed.configureTestingModule({
      providers: [AuthService, { provide: AuthApiService, useValue: authApiSpy }],
    });

    service = TestBed.inject(AuthService);
  });

  it('starts signed out, with no claims and no permissions', () => {
    expect(service.isAuthenticated()).toBeFalse();
    expect(service.claims()).toBeNull();
    expect(service.hasPermission('zones:read')).toBeFalse();
  });

  it('completes sign-in immediately when the account has no second factor', async () => {
    authApiSpy.apiAuthLoginPost.and.returnValue(
      of({
        requiresTwoFactor: false,
        mfaToken: null,
        accessToken: buildAccessToken(['zones:read']),
        expiresAtUtc: new Date().toISOString(),
        refreshToken: null,
      }),
    );

    const outcome = await service.login('someone', 'password');

    // Every sign-in asks for cookie delivery, so the refresh token never reaches page script.
    expect(authApiSpy.apiAuthLoginPost).toHaveBeenCalledWith({
      loginRequest: {
        userName: 'someone',
        password: 'password',
        rememberMe: false,
        refreshTokenDelivery: 'Cookie',
      },
    });
    expect(outcome.requiresTwoFactor).toBeFalse();
    expect(service.isAuthenticated()).toBeTrue();
    expect(service.hasPermission('zones:read')).toBeTrue();
    expect(service.hasPermission('zones:write')).toBeFalse();
  });

  it('reports a pending two-factor challenge without completing sign-in', async () => {
    authApiSpy.apiAuthLoginPost.and.returnValue(
      of({
        requiresTwoFactor: true,
        mfaToken: 'pending-mfa-token',
        accessToken: null,
        expiresAtUtc: null,
        refreshToken: null,
      }),
    );

    const outcome = await service.login('someone', 'password');

    expect(outcome).toEqual({ requiresTwoFactor: true, mfaToken: 'pending-mfa-token' });
    expect(service.isAuthenticated()).toBeFalse();
  });

  it('completes sign-in once the two-factor code is verified', async () => {
    authApiSpy.apiAuthLoginTwoFactorPost.and.returnValue(
      of({
        requiresTwoFactor: false,
        mfaToken: null,
        accessToken: buildAccessToken(['*']),
        expiresAtUtc: new Date().toISOString(),
        refreshToken: null,
      }),
    );

    await service.completeTwoFactorLogin('pending-mfa-token', '123456', 'Authenticator');

    expect(service.isAuthenticated()).toBeTrue();
    expect(authApiSpy.apiAuthLoginTwoFactorPost).toHaveBeenCalledWith({
      twoFactorLoginRequest: {
        mfaToken: 'pending-mfa-token',
        code: '123456',
        provider: 'Authenticator',
        rememberMe: false,
        refreshTokenDelivery: 'Cookie',
      },
    });
  });

  it('carries the "Remember me" choice through to the two-factor step', async () => {
    authApiSpy.apiAuthLoginPost.and.returnValue(
      of({ requiresTwoFactor: true, mfaToken: 'pending-mfa-token' }),
    );
    authApiSpy.apiAuthLoginTwoFactorPost.and.returnValue(
      of({ requiresTwoFactor: false, accessToken: buildAccessToken(['*']) }),
    );

    await service.login('someone', 'password', true);
    await service.completeTwoFactorLogin('pending-mfa-token', '123456', 'Authenticator');

    expect(authApiSpy.apiAuthLoginPost.calls.mostRecent().args[0].loginRequest.rememberMe).toBe(
      true,
    );
    expect(
      authApiSpy.apiAuthLoginTwoFactorPost.calls.mostRecent().args[0].twoFactorLoginRequest
        .rememberMe,
    ).toBe(true);
  });

  it('completes sign-in once a recovery code is redeemed', async () => {
    authApiSpy.apiAuthLoginTwoFactorPost.and.returnValue(
      of({
        requiresTwoFactor: false,
        mfaToken: null,
        accessToken: buildAccessToken(['*']),
        expiresAtUtc: new Date().toISOString(),
        refreshToken: null,
      }),
    );

    await service.completeTwoFactorLogin('pending-mfa-token', 'ABCD1234', 'RecoveryCode');

    expect(service.isAuthenticated()).toBeTrue();
    expect(authApiSpy.apiAuthLoginTwoFactorPost).toHaveBeenCalledWith({
      twoFactorLoginRequest: {
        mfaToken: 'pending-mfa-token',
        code: 'ABCD1234',
        provider: 'RecoveryCode',
        rememberMe: false,
        refreshTokenDelivery: 'Cookie',
      },
    });
  });

  it('treats a wildcard permission claim as satisfying every permission check', async () => {
    authApiSpy.apiAuthLoginPost.and.returnValue(
      of({
        requiresTwoFactor: false,
        mfaToken: null,
        accessToken: buildAccessToken(['*']),
        expiresAtUtc: new Date().toISOString(),
        refreshToken: null,
      }),
    );

    await service.login('admin', 'password');

    expect(service.hasPermission('zones:write')).toBeTrue();
    expect(service.hasPermission('anything:at-all')).toBeTrue();
  });

  it('hasAnyPermission() is satisfied by any one of the listed permissions, and by none when signed out', async () => {
    expect(service.hasAnyPermission(['zones:read', 'users:read'])).toBeFalse();

    await signIn();

    expect(service.hasAnyPermission(['users:read', 'zones:read'])).toBeTrue();
    expect(service.hasAnyPermission(['users:read', 'roles:read'])).toBeFalse();
    expect(service.hasAnyPermission([])).toBeFalse();
  });

  /** This function signs the service in, so a test starts from an authenticated session. */
  async function signIn(): Promise<void> {
    authApiSpy.apiAuthLoginPost.and.returnValue(
      of({ requiresTwoFactor: false, accessToken: buildAccessToken(['zones:read']) }),
    );
    await service.login('someone', 'password');
  }

  /** This function builds the 401 the backend answers a dead or missing refresh cookie with. */
  const unauthorized = () => new HttpErrorResponse({ status: 401 });

  it('refresh() exchanges the refresh cookie (an empty body) for a new access token', async () => {
    await signIn();
    authApiSpy.apiAuthRefreshPost.and.returnValue(
      of({ accessToken: buildAccessToken(['zones:read', 'zones:write']) }),
    );

    const refreshed = await service.refresh();

    expect(refreshed).toBeTrue();
    expect(authApiSpy.apiAuthRefreshPost).toHaveBeenCalledWith({ refreshRequest: {} });
    expect(service.hasPermission('zones:write')).toBeTrue();
  });

  it('refresh() retries a 401 once, succeeding when another tab had just rotated the cookie', async () => {
    await signIn();
    let calls = 0;
    authApiSpy.apiAuthRefreshPost.and.callFake(() =>
      ++calls === 1
        ? throwError(unauthorized)
        : of({ accessToken: buildAccessToken(['zones:read']) }),
    );

    expect(await service.refresh()).toBeTrue();
    expect(authApiSpy.apiAuthRefreshPost).toHaveBeenCalledTimes(2);
    expect(service.isAuthenticated()).toBeTrue();
  });

  it('refresh() clears the session after a second 401 -- the session has genuinely ended', async () => {
    await signIn();
    authApiSpy.apiAuthRefreshPost.and.returnValue(throwError(unauthorized));

    expect(await service.refresh()).toBeFalse();
    expect(authApiSpy.apiAuthRefreshPost).toHaveBeenCalledTimes(2);
    expect(service.isAuthenticated()).toBeFalse();
  });

  it('refresh() does not retry a non-401 failure', async () => {
    await signIn();
    authApiSpy.apiAuthRefreshPost.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 0 })),
    );

    expect(await service.refresh()).toBeFalse();
    expect(authApiSpy.apiAuthRefreshPost).toHaveBeenCalledTimes(1);
  });

  it('refresh() dedupes two concurrent calls into a single backend request', async () => {
    await signIn();
    authApiSpy.apiAuthRefreshPost.and.returnValue(
      of({ accessToken: buildAccessToken(['zones:read']) }),
    );

    const [firstResult, secondResult] = await Promise.all([service.refresh(), service.refresh()]);

    expect(firstResult).toBeTrue();
    expect(secondResult).toBeTrue();
    expect(authApiSpy.apiAuthRefreshPost).toHaveBeenCalledTimes(1);
  });

  it('restoreSession() resumes a session from the refresh cookie after a page load', async () => {
    authApiSpy.apiAuthRefreshPost.and.returnValue(
      of({ accessToken: buildAccessToken(['zones:read']) }),
    );

    await service.restoreSession();

    expect(service.isAuthenticated()).toBeTrue();
    expect(service.hasPermission('zones:read')).toBeTrue();
  });

  it('restoreSession() quietly stays signed out when there is no usable cookie', async () => {
    authApiSpy.apiAuthRefreshPost.and.returnValue(throwError(unauthorized));

    await expectAsync(service.restoreSession()).toBeResolved();
    expect(service.isAuthenticated()).toBeFalse();
  });

  it('restoreSession() does nothing when a session already exists', async () => {
    await signIn();

    await service.restoreSession();

    expect(authApiSpy.apiAuthRefreshPost).not.toHaveBeenCalled();
  });

  it('logout() clears the session immediately and revokes the cookie session server-side', async () => {
    await signIn();
    authApiSpy.apiAuthLogoutPost.and.returnValue(of({}));

    await service.logout();

    expect(service.isAuthenticated()).toBeFalse();
    expect(authApiSpy.apiAuthLogoutPost).toHaveBeenCalledWith({ refreshRequest: {} });
  });

  it('logout() still clears the session locally even when the revoke call itself fails', async () => {
    await signIn();
    authApiSpy.apiAuthLogoutPost.and.returnValue(throwError(() => new Error('network error')));

    await service.logout();

    expect(service.isAuthenticated()).toBeFalse();
  });

  it('endSessionAfterSecurityChange() signs out and leaves a notice the login page takes once', async () => {
    await signIn();
    authApiSpy.apiAuthLogoutPost.and.returnValue(of({}));

    await service.endSessionAfterSecurityChange('Your password was changed.');

    expect(service.isAuthenticated()).toBeFalse();
    expect(authApiSpy.apiAuthLogoutPost).toHaveBeenCalledWith({ refreshRequest: {} });
    expect(service.takeSignInNotice()).toBe('Your password was changed.');
    expect(service.takeSignInNotice()).toBeNull();
  });

  it('endSessionAfterSecurityChange() still signs out and leaves the notice when the revoke call fails', async () => {
    await signIn();
    authApiSpy.apiAuthLogoutPost.and.returnValue(throwError(() => new Error('network error')));

    await service.endSessionAfterSecurityChange('Two-factor authentication is on.');

    expect(service.isAuthenticated()).toBeFalse();
    expect(service.takeSignInNotice()).toBe('Two-factor authentication is on.');
  });

  it('takeSignInNotice() returns null when no security change ended the session', () => {
    expect(service.takeSignInNotice()).toBeNull();
  });

  it('applySsoAccessToken() signs in with the access token alone', () => {
    service.applySsoAccessToken(buildAccessToken(['zones:read']));

    expect(service.isAuthenticated()).toBeTrue();
  });
});
