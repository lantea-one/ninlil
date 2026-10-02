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
 * The palette and mode used when no preference can be read: before sign-in (where
 * `GET /api/appearance-settings/me` returns 401) or after a network error. They match the
 * backend defaults in `AppearanceOptions` (Teal, System).
 */
const FALLBACK_PALETTE: UiThemePaletteEnum = UiThemePaletteEnum.Teal;
const FALLBACK_MODE: UiThemeModeEnum = UiThemeModeEnum.System;

/**
 * This service runs the app's theming. It decides which of the five palettes
 * (`UiThemePaletteEnum`) and which color scheme to show, and writes them to the document root as
 * `data-coreui-theme` (light or dark) and `data-ninlil-palette`. The CSS under
 * `src/styles/themes/` selects on those two attributes. The theme switcher in the app shell
 * changes the signed-in user's preference through {@link setPalette} and {@link setMode}.
 *
 * <p>It sets `data-coreui-theme` itself instead of using CoreUI's `ColorModeService`, because
 * that service resolves `'auto'` only when its signal is set and does not follow later changes to
 * the system's `prefers-color-scheme`. This service listens for those changes
 * ({@link resolvedColorScheme}). CoreUI has no palette concept, hence the second attribute.
 *
 * <p>An `effect()` keeps both attributes in step with {@link palette} and
 * {@link resolvedColorScheme}; nothing else writes them.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly appearanceApi = inject(AppearanceApiService);

  /** The signed-in user's palette override, or `null` when none is set. */
  private readonly ownPaletteSignal = signal<UiThemePaletteEnum | null>(null);

  /** The signed-in user's mode override, or `null` when none is set. */
  private readonly ownModeSignal = signal<UiThemeModeEnum | null>(null);

  /** The palette shown now: the user's override, or the server-wide default. */
  private readonly effectivePaletteSignal = signal<UiThemePaletteEnum>(FALLBACK_PALETTE);

  /** The mode in effect now: the user's override, or the server-wide default. */
  private readonly effectiveModeSignal = signal<UiThemeModeEnum>(FALLBACK_MODE);

  /** Whether the browser's `prefers-color-scheme` media feature currently matches `dark`. */
  private readonly systemPrefersDarkSignal = signal(false);

  /** Whether {@link initialize} has completed at least once (success or fallback alike). */
  private readonly readySignal = signal(false);

  /** The live `prefers-color-scheme: dark` query this service listens to for {@link systemPrefersDarkSignal}. */
  private systemColorSchemeQuery: MediaQueryList | null = null;

  /** The palette shown now: the user's override if set, else the server-wide default. */
  readonly palette = computed(() => this.effectivePaletteSignal());

  /** The mode in effect now (possibly `System`): the user's override if set, else the server-wide default. */
  readonly mode = computed(() => this.effectiveModeSignal());

  /** Whether the user has a palette override, instead of the server-wide default. */
  readonly hasPaletteOverride = computed(() => this.ownPaletteSignal() !== null);

  /** Whether the user has a mode override, instead of the server-wide default. */
  readonly hasModeOverride = computed(() => this.ownModeSignal() !== null);

  /**
   * The color scheme on screen, always `'light'` or `'dark'`. `UiThemeModeEnum.System` follows
   * {@link systemPrefersDarkSignal}. This is the value written to `data-coreui-theme`.
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
   * This effect writes {@link resolvedColorScheme} and the palette to the document root whenever
   * either changes. It is a field initializer so it is created in the injection context.
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
   * This method reads the signed-in user's preference from `GET /api/appearance-settings/me`
   * (any signed-in user) and applies it. The response already includes the effective palette and
   * mode, with the server-wide default filled in, so no call to the `appearance:read`-gated
   * `GET /api/appearance-settings` is needed. It never throws.
   *
   * <p>It runs at startup (`provideAppInitializer` in `app.config.ts`) and again after sign-in.
   * Without a session it falls back to {@link FALLBACK_PALETTE} and {@link FALLBACK_MODE}.
   */
  async initialize(): Promise<void> {
    try {
      const preference = await firstValueFrom(this.appearanceApi.apiAppearanceSettingsMeGet());
      this.applyPreference(preference);
    } catch {
      // No session yet (this endpoint returns 401 on the login page) or a network error: show
      // the built-in default. A later initialize() after sign-in picks up the preference.
      this.ownPaletteSignal.set(null);
      this.ownModeSignal.set(null);
      this.effectivePaletteSignal.set(FALLBACK_PALETTE);
      this.effectiveModeSignal.set(FALLBACK_MODE);
    } finally {
      this.readySignal.set(true);
    }
  }

  /**
   * This method sets the signed-in user's palette override, or clears it with `null`, through
   * `PATCH /api/appearance-settings/me`, and applies the response at once. The theme switcher
   * calls it. Errors propagate to the caller.
   */
  async setPalette(palette: UiThemePaletteEnum | null): Promise<void> {
    const preference = await firstValueFrom(
      this.appearanceApi.apiAppearanceSettingsMePatch({ body: { palette } }),
    );
    this.applyPreference(preference);
  }

  /**
   * This method sets the signed-in user's mode override, or clears it with `null`, through
   * `PATCH /api/appearance-settings/me`, and applies the response at once. The theme switcher
   * calls it. Errors propagate to the caller.
   */
  async setMode(mode: UiThemeModeEnum | null): Promise<void> {
    const preference = await firstValueFrom(
      this.appearanceApi.apiAppearanceSettingsMePatch({ body: { mode } }),
    );
    this.applyPreference(preference);
  }

  /** This method copies a `UserAppearancePreferenceDetail` response into the signals. */
  private applyPreference(preference: UserAppearancePreferenceDetail): void {
    this.ownPaletteSignal.set(preference.palette ?? null);
    this.ownModeSignal.set(preference.mode ?? null);
    this.effectivePaletteSignal.set(preference.effectivePalette ?? FALLBACK_PALETTE);
    this.effectiveModeSignal.set(preference.effectiveMode ?? FALLBACK_MODE);
  }

  /**
   * This method listens to the `(prefers-color-scheme: dark)` media query for the app's lifetime
   * and keeps {@link systemPrefersDarkSignal} in step, so `System` mode follows later changes to
   * the operating system's preference.
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
