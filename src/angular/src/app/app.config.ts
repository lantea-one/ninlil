import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideApi } from './api-client';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { BrandingService } from './core/branding/branding.service';
import { provideCoreUiIcons } from './core/icons/core-ui-icons';
import { ThemeService } from './core/theme/theme.service';
import { routes } from './app.routes';

/**
 * The root providers of the console: routing, HTTP with the auth interceptor, the API client,
 * the icon set and the start-up session restore.
 */
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideAnimationsAsync(),
    provideHttpClient(withInterceptors([authInterceptor])),
    // The generated client defaults to "http://localhost" (api-client/api.base.service.ts). An
    // empty base path makes every request relative, so it goes to the origin that served the
    // app; the backend serves the app and the API from the same origin.
    provideApi(''),
    provideCoreUiIcons(),
    // Restore the session from the refresh cookie first (see AuthService.restoreSession), then
    // load the user's appearance preference and the site title. Both need the restored session,
    // or the app would show the default palette and title. Without a session they fall back to
    // the built-in defaults. All of this finishes before the first render and the first route
    // guard, so the first page, including the login page, appears in the right theme.
    provideAppInitializer(async () => {
      const authService = inject(AuthService);
      const themeService = inject(ThemeService);
      const brandingService = inject(BrandingService);
      await authService.restoreSession();
      await Promise.all([themeService.initialize(), brandingService.initialize()]);
    }),
  ],
};
