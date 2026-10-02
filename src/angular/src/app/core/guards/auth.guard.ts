import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../auth/auth.service';

/**
 * This guard protects the signed-in part of the app (the app shell route in `app.routes.ts`). A
 * visitor without a session goes to `/login`, with the attempted URL in a `returnUrl` query
 * parameter so the login page can send them on after sign-in. It only checks that a session
 * exists; {@link permissionGuard} checks permissions.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isAuthenticated()) return true;

  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};
