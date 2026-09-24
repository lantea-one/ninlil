import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../auth/auth.service';

/**
 * This function builds a route guard requiring a specific permission claim, mirroring the
 * backend's own `[RequirePermission("resource:action")]` model
 * (`Ninlil.Dns.Identity.Authorization.RequirePermissionAttribute`). An unauthenticated
 * caller is sent to `/login` first (the same outcome {@link authGuard} produces); an
 * authenticated caller lacking the permission is sent to `/forbidden` instead -- a real route
 * block, not merely a hidden nav item: a permission-gated page stays blocked even when navigated
 * to directly (a typed-in URL, a bookmark, a link from outside the nav).
 */
export function permissionGuard(permission: string): CanActivateFn {
  return (_route, state) => {
    const authService = inject(AuthService);
    const router = inject(Router);

    if (!authService.isAuthenticated()) {
      return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    }

    if (authService.hasPermission(permission)) return true;

    return router.createUrlTree(['/forbidden']);
  };
}
