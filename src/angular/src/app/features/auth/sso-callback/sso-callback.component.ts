import { Component, OnInit, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardHeaderComponent,
  SpinnerComponent,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { AuthService } from '../../../core/auth/auth.service';
import { BrandingService } from '../../../core/branding/branding.service';
import { ThemeService } from '../../../core/theme/theme.service';

/**
 * This component is the landing page a completed OpenID Connect sign-in redirects the browser
 * back to -- `Ninlil.Dns.Identity.Extension.ServiceCollectionAuthenticationExtension`'s own
 * `OnTokenValidated` event, after minting a real Ninlil access/refresh token pair for the
 * federated account, sets the refresh token as the HttpOnly refresh cookie and sends the browser
 * to `/sso-callback#access_token=...&expires_at=...`. A top-level route sibling to `login`/`forgot-password`/`reset-password`
 * (see `app.routes.ts`), reachable with no session at all -- that is exactly the state this page
 * exists to end.
 *
 * <p>The access token rides in the URL **fragment**, never a query parameter -- deliberately, on the
 * backend's own side, so they never land in a server access log or an intermediate proxy's own
 * logs. `window.location.hash` is read directly rather than through `ActivatedRoute`'s own
 * `queryParamMap` for exactly that reason: Angular's router treats the fragment as router-level
 * navigation state, not a queryable param map, and this page needs the raw string.
 *
 * <p>Once parsed, the access token is applied through {@link AuthService.applySsoAccessToken} -- the exact
 * same in-memory token-store path a completed local sign-in already goes through
 * (`AuthService.login`/`completeTwoFactorLogin`/`refresh`), never a second, parallel one -- then
 * this page re-runs {@link ThemeService.initialize}/{@link BrandingService.initialize} for the
 * identical reason `LoginComponent.navigateAfterSignIn` already documents in detail: both ran
 * anonymously before this page even loaded, so only a real, authenticated re-run actually pulls
 * in this account's own persisted preferences.
 */
@Component({
  selector: 'app-sso-callback',
  imports: [
    RouterLink,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardHeaderComponent,
    IconDirective,
    SpinnerComponent,
  ],
  templateUrl: './sso-callback.component.html',
  styleUrl: './sso-callback.component.scss',
})
export class SsoCallbackComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly themeService = inject(ThemeService);
  private readonly brandingService = inject(BrandingService);

  /** Whether the URL fragment this page loaded with carried neither a valid token pair nor was ever expected to. */
  readonly failed = signal(false);

  ngOnInit(): void {
    void this.completeSignIn();
  }

  /**
   * This method parses `access_token` out of `window.location.hash`, applies it, and navigates
   * to the dashboard -- or, if it is missing (a malformed or replayed link, never a real redirect
   * this app's own backend would have produced), shows this page's own error state instead of
   * silently landing on a broken dashboard. The refresh token never appears here: the same
   * redirect response already set it as the HttpOnly refresh cookie.
   */
  private async completeSignIn(): Promise<void> {
    const fragment = window.location.hash.replace(/^#/, '');
    const params = new URLSearchParams(fragment);
    const accessToken = params.get('access_token');

    if (!accessToken) {
      this.failed.set(true);
      return;
    }

    // Drop the token from the address bar and history now that it has been read.
    history.replaceState(null, '', window.location.pathname);

    this.authService.applySsoAccessToken(accessToken);

    await Promise.all([this.themeService.initialize(), this.brandingService.initialize()]);
    await this.router.navigateByUrl('/dashboard');
  }
}
