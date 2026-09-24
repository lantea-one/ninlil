import { DOCUMENT } from '@angular/common';
import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  AppearanceSettingsService as AppearanceApiService,
  UiThemeModeEnum,
  UiThemePaletteEnum,
  UserAppearancePreferenceDetail,
} from '../../api-client';

/**
 * This app's own compiled-in fallback palette/mode, used whenever a real preference cannot be
 * read from the backend -- an anonymous caller (e.g. still on the login page, where
 * `GET /api/appearance-settings/me` 401s since it requires a signed-in session) or a transient
 * network failure. Deliberately mirrors the backend's own compiled-in default
 * (`UiThemePaletteEnum.Teal`/`UiThemeModeEnum.System`, see `AppearanceOptions`) rather than
 * inventing a separate frontend-only default that could drift from it.
 */
const FALLBACK_PALETTE: UiThemePaletteEnum = UiThemePaletteEnum.Teal;
const FALLBACK_MODE: UiThemeModeEnum = UiThemeModeEnum.System;

/**
 * This service is this app's entire client-side theming engine: it resolves which of the 5
 * palettes (`UiThemePaletteEnum`) and which light/dark rendering ({@link resolvedColorScheme},
 * resolving `UiThemeModeEnum.System` against the browser's own live `prefers-color-scheme`
 * state) the document should currently render in, and applies that choice to the document root
 * as the `data-coreui-theme`/`data-ninlil-palette` attribute pair the CSS token catalog under
 * `src/styles/themes/` selects on.
 *
 * <p>Two attributes, not one, are needed because CoreUI for Angular's own `ColorModeService`
 * (see `app.config.ts`'s own doc comment) already owns `data-coreui-theme` as a plain
 * light/dark switch -- this service sets that same attribute directly (rather than going
 * through `ColorModeService` itself) because that service's own `colorMode` signal only
 * re-resolves its `'auto'` value at the moment the signal is *set*, not continuously: it has no
 * live `prefers-color-scheme` listener of its own, so relying on it would silently stop
 * tracking a system theme change made after the initial resolution. `data-ninlil-palette` is
 * this app's own second attribute, carrying which of the 5 accent palettes is active -- CoreUI
 * ships no equivalent concept, since its own free tier has only one built-in accent color.
 *
 * <p>On construction, an internal `effect()` (the same "reactive field initializer" pattern
 * `ColorModeService` itself uses) keeps the document root's own two attributes in sync with
 * {@link palette}/{@link resolvedColorScheme} automatically, through Angular's zoneless
 * signal-based change detection -- no manual DOM writes or change-detection triggering are ever
 * needed outside this service.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly appearanceApi = inject(AppearanceApiService);

  /** The current caller's own palette override, or `null` when none is set. */
  private readonly ownPaletteSignal = signal<UiThemePaletteEnum | null>(null);

  /** The current caller's own mode override, or `null` when none is set. */
  private readonly ownModeSignal = signal<UiThemeModeEnum | null>(null);

  /** The palette actually rendered right now -- the caller's own override, or the inherited default. */
  private readonly effectivePaletteSignal = signal<UiThemePaletteEnum>(FALLBACK_PALETTE);

  /** The mode actually rendered right now -- the caller's own override, or the inherited default. */
  private readonly effectiveModeSignal = signal<UiThemeModeEnum>(FALLBACK_MODE);

  /** Whether the browser's own `prefers-color-scheme` media feature currently matches `dark`. */
  private readonly systemPrefersDarkSignal = signal(false);

  /** Whether {@link initialize} has completed at least once (success or fallback alike). */
  private readonly readySignal = signal(false);

  /** The live `prefers-color-scheme: dark` query this service listens to for {@link systemPrefersDarkSignal}. */
  private systemColorSchemeQuery: MediaQueryList | null = null;

  /** The palette currently being rendered -- the caller's own override if set, else the inherited default. */
  readonly palette = computed(() => this.effectivePaletteSignal());

  /** The mode currently in effect (possibly `System`) -- the caller's own override if set, else the inherited default. */
  readonly mode = computed(() => this.effectiveModeSignal());

  /** Whether the caller has their own palette override set (as opposed to inheriting the global default). */
  readonly hasPaletteOverride = computed(() => this.ownPaletteSignal() !== null);

  /** Whether the caller has their own mode override set (as opposed to inheriting the global default). */
  readonly hasModeOverride = computed(() => this.ownModeSignal() !== null);

  /**
   * The color scheme actually painted on screen right now -- always `'light'` or `'dark'`, with
   * `UiThemeModeEnum.System` resolved live against {@link systemPrefersDarkSignal}. This is the
   * value written to the `data-coreui-theme` attribute.
   */
  readonly resolvedColorScheme = computed<'light' | 'dark'>(() => {
    const mode = this.effectiveModeSignal();
    if (mode === UiThemeModeEnum.Light) return 'light';
    if (mode === UiThemeModeEnum.Dark) return 'dark';
    return this.systemPrefersDarkSignal() ? 'dark' : 'light';
  });

  /** Whether {@link initialize} has completed at least once. */
  readonly ready = computed(() => this.readySignal());

  /**
   * This field initializer keeps the document root's theming attributes synchronized with
   * {@link palette}/{@link resolvedColorScheme} for as long as this service exists (it does,
   * for the app's entire lifetime, since it is `providedIn: 'root'`) -- constructed here rather
   * than in the constructor body so it runs within this service's own injection context, the
   * same pattern CoreUI's own `ColorModeService` uses for its equivalent effect.
   */
  private readonly applyThemeToDocumentEffect = effect(() => {
    const root = this.document.documentElement;
    root.setAttribute('data-coreui-theme', this.resolvedColorScheme());
    root.setAttribute('data-ninlil-palette', this.effectivePaletteSignal());
  });

  constructor() {
    this.watchSystemColorScheme();
  }

  /**
   * This method reads the signed-in caller's own appearance preference from
   * `GET /api/appearance-settings/me` and applies it. That endpoint's own response already
   * carries the fully-resolved effective palette/mode (falling back to this host's own global
   * default for whichever field, if either, the caller has not overridden) -- so, unlike a
   * caller managing the global default itself, this service never needs a second call to the
   * admin-only `GET /api/appearance-settings` endpoint to complete that fallback.
   *
   * <p>Call this once at application startup (wired via `provideAppInitializer` in
   * `app.config.ts`) and again after a successful sign-in -- the first call, made before any
   * session exists, always falls through to {@link FALLBACK_PALETTE}/{@link FALLBACK_MODE} since
   * `GET /me` requires authentication; re-calling it once a real session exists picks up the
   * signed-in caller's real preference.
   */
  async initialize(): Promise<void> {
    try {
      const preference = await firstValueFrom(this.appearanceApi.apiAppearanceSettingsMeGet());
      this.applyPreference(preference);
    } catch {
      // No session yet (an anonymous caller on the login page, where this endpoint 401s) or a
      // transient failure -- render with this app's own compiled-in default rather than leaving
      // the document unthemed. Safe to retry via another `initialize()` call once signed in.
      this.ownPaletteSignal.set(null);
      this.ownModeSignal.set(null);
      this.effectivePaletteSignal.set(FALLBACK_PALETTE);
      this.effectiveModeSignal.set(FALLBACK_MODE);
    } finally {
      this.readySignal.set(true);
    }
  }

  /**
   * This method sets (or, passing `null`, clears) the signed-in caller's own palette override,
   * via `PATCH /api/appearance-settings/me`, and applies the response immediately -- the
   * switcher UI's own palette picker calls this directly, and the resulting theme change is
   * visible with no page reload.
   */
  async setPalette(palette: UiThemePaletteEnum | null): Promise<void> {
    const preference = await firstValueFrom(
      this.appearanceApi.apiAppearanceSettingsMePatch({ body: { palette } }),
    );
    this.applyPreference(preference);
  }

  /**
   * This method sets (or, passing `null`, clears) the signed-in caller's own mode override, via
   * `PATCH /api/appearance-settings/me`, and applies the response immediately -- the switcher
   * UI's own mode picker calls this directly, and the resulting theme change is visible with no
   * page reload.
   */
  async setMode(mode: UiThemeModeEnum | null): Promise<void> {
    const preference = await firstValueFrom(
      this.appearanceApi.apiAppearanceSettingsMePatch({ body: { mode } }),
    );
    this.applyPreference(preference);
  }

  /** This method applies a `UserAppearancePreferenceDetail` response to this service's own signals. */
  private applyPreference(preference: UserAppearancePreferenceDetail): void {
    this.ownPaletteSignal.set(preference.palette ?? null);
    this.ownModeSignal.set(preference.mode ?? null);
    this.effectivePaletteSignal.set(preference.effectivePalette ?? FALLBACK_PALETTE);
    this.effectiveModeSignal.set(preference.effectiveMode ?? FALLBACK_MODE);
  }

  /**
   * This method opens a live `(prefers-color-scheme: dark)` media query and keeps
   * {@link systemPrefersDarkSignal} in sync with it for the app's entire lifetime, so
   * `UiThemeModeEnum.System` keeps tracking the operating system's own preference after the
   * initial resolution -- not just once at load, the gap {@link ThemeService}'s own doc comment
   * explains CoreUI's own `ColorModeService` leaves open for `'auto'`.
   */
  private watchSystemColorScheme(): void {
    const view = this.document.defaultView;
    if (!view) return;

    this.systemColorSchemeQuery = view.matchMedia('(prefers-color-scheme: dark)');
    this.systemPrefersDarkSignal.set(this.systemColorSchemeQuery.matches);
    this.systemColorSchemeQuery.addEventListener('change', (event) => {
      this.systemPrefersDarkSignal.set(event.matches);
    });
  }
}
