import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { SETTINGS_NAV_GROUPS } from './settings-nav';

/**
 * This guard protects `/settings` itself, whose page is the DNS security presets list. A user
 * holding `presets:read` sees it; anyone else who may open the settings area (the parent route
 * already requires one of the tabs' permissions) is sent to the first tab they can read, rather
 * than to `/forbidden` just for following the "Presets & Settings" nav link.
 */
export const settingsLandingGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.hasPermission('presets:read')) return true;

  const firstReadableTab = SETTINGS_NAV_GROUPS.flatMap((group) => group.tabs).find((tab) =>
    authService.hasPermission(tab.permission),
  );

  return router.parseUrl(firstReadableTab?.path ?? '/forbidden');
};
