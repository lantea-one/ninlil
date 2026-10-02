import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { IconSetService } from '@coreui/icons-angular';
import {
  cilAccountLogout,
  cilAddressBook,
  cilArrowLeft,
  cilBan,
  cilBellExclamation,
  cilBook,
  cilCamera,
  cilCheck,
  cilCheckCircle,
  cilChevronBottom,
  cilCircle,
  cilClock,
  cilCloud,
  cilCloudDownload,
  cilCloudUpload,
  cilCode,
  cilCog,
  cilColorPalette,
  cilContact,
  cilContrast,
  cilCopy,
  cilDevices,
  cilEnvelopeClosed,
  cilEthernet,
  cilFilter,
  cilGamepad,
  cilGroup,
  cilHeadphones,
  cilHistory,
  cilHome,
  cilLan,
  cilLaptop,
  cilLightbulb,
  cilLink,
  cilList,
  cilListRich,
  cilLockLocked,
  cilLockUnlocked,
  cilMediaPlay,
  cilMediaStop,
  cilMemory,
  cilMenu,
  cilMobile,
  cilMoon,
  cilPencil,
  cilPlus,
  cilPrint,
  cilReload,
  cilRouter,
  cilScreenDesktop,
  cilSearch,
  cilSettings,
  cilShareAll,
  cilShieldAlt,
  cilSpeaker,
  cilSpeedometer,
  cilStar,
  cilStorage,
  cilSun,
  cilSwapHorizontal,
  cilTablet,
  cilTags,
  cilTrash,
  cilTv,
  cilUser,
  cilWarning,
  cilWatch,
  cilWifiSignal4,
  cilX,
  cilXCircle,
} from '@coreui/icons';
import { provideApi } from './api-client';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { BrandingService } from './core/branding/branding.service';
import { ThemeService } from './core/theme/theme.service';
import { routes } from './app.routes';

/**
 * The CoreUI icons the app uses. How CoreUI is set up:
 *
 * - Its components and directives are standalone, so each page imports what it uses. No
 *   `provideXxx()` or `NgModule` is needed.
 * - `IconSetService` starts with no icons. An app initializer below fills it from this object,
 *   which takes icon data from `@coreui/icons` (`@coreui/icons-angular` supplies only the `cIcon`
 *   component and the service). Add each new icon here instead of importing the whole `freeSet`,
 *   so the bundle carries only the icons in use. An icon missing from this list renders blank.
 * - CoreUI's compiled CSS is a global stylesheet in the `styles` array of `angular.json` (build
 *   and test), loaded before `src/styles.scss`. It is not pulled in through Sass, because
 *   Bootstrap's SCSS relies on `@import`, which does not mix well with the `@use`-based theming in
 *   `styles.scss`.
 */
const coreUiIcons = {
  cilAccountLogout,
  cilAddressBook,
  cilArrowLeft,
  cilBan,
  cilBellExclamation,
  cilBook,
  cilCamera,
  cilCheck,
  cilCheckCircle,
  cilChevronBottom,
  cilCircle,
  cilClock,
  cilCloud,
  cilCloudDownload,
  cilCloudUpload,
  cilCode,
  cilCog,
  cilColorPalette,
  cilContact,
  cilContrast,
  cilCopy,
  cilDevices,
  cilEnvelopeClosed,
  cilEthernet,
  cilFilter,
  cilGamepad,
  cilGroup,
  cilHeadphones,
  cilHistory,
  cilHome,
  cilLan,
  cilLaptop,
  cilLightbulb,
  cilLink,
  cilList,
  cilListRich,
  cilLockLocked,
  cilLockUnlocked,
  cilMediaPlay,
  cilMediaStop,
  cilMemory,
  cilMenu,
  cilMobile,
  cilMoon,
  cilPencil,
  cilPlus,
  cilPrint,
  cilReload,
  cilRouter,
  cilScreenDesktop,
  cilSearch,
  cilSettings,
  cilShareAll,
  cilShieldAlt,
  cilSpeaker,
  cilSpeedometer,
  cilStar,
  cilStorage,
  cilSun,
  cilSwapHorizontal,
  cilTablet,
  cilTags,
  cilTrash,
  cilTv,
  cilUser,
  cilWarning,
  cilWatch,
  cilWifiSignal4,
  cilX,
  cilXCircle,
};

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
    provideAppInitializer(() => {
      inject(IconSetService).icons = coreUiIcons;
    }),
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
