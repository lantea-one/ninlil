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
 * CoreUI for Angular's bootstrap pattern, applied here as this app's own established convention:
 *
 * - No CoreUI-specific `provideXxx()` function or `NgModule` import is needed to use its
 *   components/directives (`CardComponent`, `ButtonDirective`, `FormControlDirective`, etc.) --
 *   every one of them is a standalone Angular 22 component/directive, imported directly into a
 *   consuming component's own `imports` array, and `ColorModeService` (light/dark theme) is
 *   `providedIn: 'root'` so it needs no explicit provider either.
 * - The one piece of real app-wide setup CoreUI needs is its **icon set**: `IconSetService` is
 *   also `providedIn: 'root'`, but its `icons` property starts empty -- population happens once,
 *   here, via `provideAppInitializer` (the functional replacement for the older `APP_INITIALIZER`
 *   token), assigning whichever icons (from the separate, tree-shakeable `@coreui/icons` data
 *   package -- distinct from the Angular integration package `@coreui/icons-angular`, which only
 *   supplies the `cIcon` directive/component and the service, no actual icon artwork) this app
 *   actually references. Add new icon names to this object as new pages need them, rather
 *   than importing the whole `freeSet`, to keep the icon payload tree-shaken to what's used.
 * - CoreUI's compiled CSS (Bootstrap 5 reboot + grid + every component's styling, including the
 *   `[data-coreui-theme]` light/dark custom-property tokens `ColorModeService` toggles) is wired
 *   as a plain global stylesheet in `angular.json`'s `styles` array (both the `build` and `test`
 *   targets), loaded *before* `src/styles.scss` -- not via a Sass `@use`/`@import`, since
 *   Bootstrap's own SCSS source assumes classic `@import` semantics that don't mix cleanly with
 *   `src/styles.scss`'s own modern `@use`-based theming (see that file's own doc comment for its
 *   5-palette x light/dark catalog) if pulled into the same Sass compilation instead.
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
    // The generated API client's own BaseService defaults to "http://localhost" when no base
    // path is configured (see api-client/api.base.service.ts) -- an empty string here makes
    // every generated request relative, so it resolves against whatever origin this app is
    // actually served from, matching this project's own same-origin hosting decision.
    provideApi(''),
    provideAppInitializer(() => {
      inject(IconSetService).icons = coreUiIcons;
    }),
    // Resumes a session from the refresh cookie first (a full page reload keeps the operator
    // signed in -- see `AuthService.restoreSession`), then resolves the signed-in caller's own
    // appearance preference and custom site title. The order matters: both of those read
    // per-user settings, so they must see the restored session, not an anonymous one, or the app
    // would render its default palette and title until the next navigation. With no session
    // (still on the login page), each falls back to this app's own compiled-in default -- see
    // `ThemeService.initialize`/`BrandingService.initialize`. Everything here finishes before the
    // first real render and the first route guard, so every CoreUI component -- including the
    // login page itself -- renders in the right palette/mode rather than flashing a default.
    provideAppInitializer(async () => {
      const authService = inject(AuthService);
      const themeService = inject(ThemeService);
      const brandingService = inject(BrandingService);
      await authService.restoreSession();
      await Promise.all([themeService.initialize(), brandingService.initialize()]);
    }),
  ],
};
