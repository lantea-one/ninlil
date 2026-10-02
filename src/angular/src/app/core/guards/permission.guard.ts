import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../auth/auth.service';

/**
 * This function builds a route guard that requires one permission, matching the backend's
 * `[RequirePermission("resource:action")]` checks. A visitor without a session goes to `/login`,
 * as with {@link authGuard}. A signed-in user without the permission goes to `/forbidden`. Hiding
 * a nav item is not enough on its own: the guard also blocks a typed-in URL or a bookmark. The
 * backend still enforces the permission on every API call.
 */
export function permissionGuard(permission: string): CanActivateFn {
  return anyPermissionGuard([permission]);
}

/**
 * This function builds a route guard like {@link permissionGuard} that any one of several
 * permissions satisfies. It suits a parent route such as `/settings` whose children each check
 * their permission.
 */
export function anyPermissionGuard(permissions: readonly string[]): CanActivateFn {
  return (_route, state) => {
    const authService = inject(AuthService);
    const router = inject(Router);

    if (!authService.isAuthenticated()) {
      return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    }

    if (permissions.some((permission) => authService.hasPermission(permission))) return true;

    return router.createUrlTree(['/forbidden']);
  };
}
