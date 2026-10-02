import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * This constant lists the anonymous `AuthController` endpoints that handle sign-in and session
 * tokens. Requests to them never carry an access token, and a 401 from one of them never
 * triggers a silent refresh: a wrong password is an ordinary 401, not an expired session.
 *
 * <p>It must stay a list of paths, not an `/api/auth/` prefix. `TwoFactorController` is routed
 * under `api/auth/two-factor` and requires a signed-in user, so a prefix match would strip its
 * Authorization header and break two-factor setup with a 401. The spec file has regression tests
 * for this.
 */
const ANONYMOUS_AUTH_ENDPOINTS = [
  '/api/auth/login',
  '/api/auth/login/two-factor',
  '/api/auth/refresh',
  '/api/auth/logout',
];

/**
 * This interceptor, registered in `app.config.ts`, adds the in-memory access token as a Bearer
 * header to every API request except the endpoints in `ANONYMOUS_AUTH_ENDPOINTS`.
 *
 * <p>When a request that carried a token gets a 401, the token has most likely expired. The
 * interceptor then calls {@link AuthService.refresh} once and retries the request with the new
 * token. If the refresh fails, or the retry also gets a 401, it sends the browser to `/login`
 * and passes the error on to the caller. A retry is never retried again: a fresh token that still
 * gets a 401 will not do better a second time.
 *
 * <p>A request sent without a token is never refreshed or redirected, even on a 401. There is no
 * session for that 401 to have expired, and such callers handle it themselves. For example,
 * `BrandingService.initialize` and `ThemeService.initialize` run at startup before anyone signs
 * in. Redirecting there would pull a visitor off public pages such as `/forgot-password` and
 * `/reset-password` as soon as they load.
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
