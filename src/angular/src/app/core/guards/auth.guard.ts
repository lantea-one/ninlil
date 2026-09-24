import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../auth/auth.service';

/**
 * This guard blocks navigation to any route requiring a signed-in session, redirecting an
 * unauthenticated caller to `/login` with the attempted URL preserved as a `returnUrl` query
 * parameter, so the login page can send them back on to where they were headed.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isAuthenticated()) return true;

  return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};
