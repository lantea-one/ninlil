import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BrandingSettingsService as BrandingApiService } from '../../api-client';

/**
 * This app's own compiled-in default page title -- the literal `<title>` this project's
 * `index.html` sets statically. Used as this service's own fallback whenever no custom site title
 * is configured (or cannot be read yet, e.g. an anonymous caller still on the login page), so
 * {@link BrandingService.initialize} never leaves the document with a blank or stale title.
 */
const DEFAULT_SITE_TITLE = 'Ninlil Admin';

/**
 * This service applies this host's own global branding (`BrandingOptions`: a custom site
 * title/logo) to the running SPA -- specifically, `document.title` -- mirroring
 * {@link ThemeService}'s own "resolve once at app startup, re-resolve once more after sign-in"
 * shape exactly, since both read from a self-service endpoint that only succeeds for an
 * authenticated caller (`GET /api/branding-settings/effective`, gated by `[Authorize]` alone --
 * see `BrandingSettingsController.GetEffective`'s own doc comment for why no specific permission
 * is required).
 *
 * <p>The logo itself ({@link logoDataUri}) is exposed as a signal for any future consumer (a
 * branding mark somewhere in the app shell, for example), but only the page title actually
 * updates live today -- no such consumer exists yet, so this signal is currently only ever read
 * by this service's own tests.
 */
@Injectable({ providedIn: 'root' })
export class BrandingService {
  private readonly document = inject(DOCUMENT);
  private readonly brandingApi = inject(BrandingApiService);

  private readonly siteTitleSignal = signal<string | null>(null);
  private readonly logoDataUriSignal = signal<string | null>(null);

  /** The custom site title currently applied, or `null` when none is configured. */
  readonly siteTitle = computed(() => this.siteTitleSignal());

  /** The custom logo currently configured, as a data URI, or `null` when none is configured. */
  readonly logoDataUri = computed(() => this.logoDataUriSignal());

  /**
   * This method reads `GET /api/branding-settings/effective` and applies the result to
   * `document.title` (falling back to {@link DEFAULT_SITE_TITLE} when no custom title is
   * configured, or when the read itself fails -- an anonymous caller still on the login page, or a
   * transient network failure).
   *
   * <p>Call this once at application startup (wired via `provideAppInitializer` in
   * `app.config.ts`) and again after a successful sign-in (`LoginComponent.navigateAfterSignIn`,
   * alongside `ThemeService.initialize`) -- the first call, made before any session exists, always
   * falls through to the fallback title since this endpoint requires authentication; re-calling it
   * once a real session exists picks up the real configured title.
   */
  async initialize(): Promise<void> {
    try {
      const effective = await firstValueFrom(this.brandingApi.apiBrandingSettingsEffectiveGet());
      this.siteTitleSignal.set(effective.siteTitle ?? null);
      this.logoDataUriSignal.set(effective.logoDataUri ?? null);
    } catch {
      this.siteTitleSignal.set(null);
      this.logoDataUriSignal.set(null);
    } finally {
      this.document.title = this.siteTitleSignal() ?? DEFAULT_SITE_TITLE;
    }
  }
}
