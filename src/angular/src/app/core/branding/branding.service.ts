import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BrandingSettingsService as BrandingApiService } from '../../api-client';

/**
 * The default page title, the same as the `<title>` in `index.html`. It applies when no custom
 * title is configured or the branding cannot be read yet (on the login page, for example).
 */
const DEFAULT_SITE_TITLE = 'Ninlil Admin';

/**
 * This service applies the server's branding (`BrandingOptions`: a custom site title and logo) to
 * the app. It reads `GET /api/branding-settings/effective`, which any signed-in user may call
 * (`[Authorize]`, no permission). Like {@link ThemeService}, it runs once at startup and again
 * after sign-in, because the first call fails without a session. The Branding settings page
 * edits these values.
 *
 * <p>Only the page title is applied. {@link logoDataUri} holds the logo, but no component
 * displays it.
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
   * This method reads `GET /api/branding-settings/effective` and sets `document.title`, falling
   * back to {@link DEFAULT_SITE_TITLE} when no title is configured or the read fails (no session
   * yet, or a network error). It never throws.
   *
   * <p>It runs at startup (`provideAppInitializer` in `app.config.ts`) and again after sign-in
   * (`LoginComponent.navigateAfterSignIn`, with `ThemeService.initialize`). The startup call
   * usually has no session yet; it still has one when a refresh cookie restored the session.
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
