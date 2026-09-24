import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * This constant lists the exact paths of every `[AllowAnonymous]` `AuthController` endpoint --
 * `/api/auth/login`, `/api/auth/login/two-factor`, `/api/auth/refresh`, `/api/auth/logout` --
 * none of them attach a (possibly stale) access token, and a 401 from one of them is never
 * treated as "the session expired, try a silent refresh" (a bad-password login attempt is a
 * normal 401, not a token expiry). This is deliberately a list of exact paths rather than a
 * single `/api/auth/` prefix fragment: `TwoFactorController` is also routed under
 * `api/auth/two-factor` but is fully `[Authorize]`-gated self-service (TOTP setup, recovery
 * codes, etc.) -- a blanket `/api/auth/` prefix match would silently strip the Authorization
 * header from every one of its requests too, breaking the entire self-service 2FA feature with
 * an opaque 401 (a real regression this project once shipped; see `auth.interceptor.spec.ts`'s
 * own two-factor-scoped regression tests for the exact shape).
 */
const ANONYMOUS_AUTH_ENDPOINTS = [
  '/api/auth/login',
  '/api/auth/login/two-factor',
  '/api/auth/refresh',
  '/api/auth/logout',
];

/**
 * This interceptor attaches the current in-memory access token to every outgoing request (other
 * than the auth endpoints themselves), and, on a 401 response *from a request this session
 * actually believed was authenticated*, attempts exactly one silent refresh-and-retry before
 * giving up and routing to `/login`. A request that
 * still 401s after a successful refresh (a permission-claim mismatch on an otherwise-valid
 * token, for example) is *not* retried a second time; its own 401 is simply passed through to
 * the caller as-is, since a second retry would not plausibly succeed where a fresh token already
 * failed.
 *
 * <p>A request that was already anonymous when it was made (no access token attached at all --
 * the case for every request made while no session exists, including on a page load whose
 * refresh cookie turned out to be missing or dead) never triggers the refresh-and-redirect-to-`/login`
 * path, even on a 401: there is no session for that 401 to mean "expired," and a caller in this
 * position (an app-startup initializer probing a self-service `/me`-style endpoint before any
 * sign-in exists, for example -- see `BrandingService.initialize`/`ThemeService.initialize`) has
 * already designed its own graceful, request-scoped fallback for exactly this outcome. Routing
 * away to `/login` regardless used to hijack navigation on a cold load of a genuinely public
 * route with no session at all -- `/forgot-password`, or the emailed `/reset-password` link
 * itself -- the moment either page's own app-startup initializer call predictably 401ed, before
 * the visitor had any chance to use the page they actually opened.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  const isAuthEndpoint = ANONYMOUS_AUTH_ENDPOINTS.some((path) => request.url.includes(path));
  const accessToken = authService.accessToken;

  const authorizedRequest =
    accessToken && !isAuthEndpoint
      ? request.clone({ setHeaders: { Authorization: `Bearer ${accessToken}` } })
      : request;

  return next(authorizedRequest).pipe(
    catchError((error: unknown) => {
      if (
        !(error instanceof HttpErrorResponse) ||
        error.status !== 401 ||
        isAuthEndpoint ||
        !accessToken
      ) {
        return throwError(() => error);
      }

      return from(authService.refresh()).pipe(
        switchMap((refreshed) => {
          if (!refreshed) {
            void router.navigateByUrl('/login');
            return throwError(() => error);
          }

          const retriedRequest = request.clone({
            setHeaders: { Authorization: `Bearer ${authService.accessToken}` },
          });

          return next(retriedRequest).pipe(
            catchError((retryError: unknown) => {
              if (retryError instanceof HttpErrorResponse && retryError.status === 401) {
                void router.navigateByUrl('/login');
              }
              return throwError(() => retryError);
            }),
          );
        }),
      );
    }),
  );
};
