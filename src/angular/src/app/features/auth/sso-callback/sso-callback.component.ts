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
 * This component is the public `/sso-callback` page, where a completed OpenID Connect sign-in
 * lands. After validating the identity provider's token, the backend
 * (`ServiceCollectionAuthenticationExtension`, `OnTokenValidated`) issues a Ninlil session, sets
 * the refresh token as the HttpOnly cookie and redirects to
 * `/sso-callback#access_token=...&expires_at=...`.
 *
 * <p>The access token is in the URL fragment, which browsers never send to a server, so it stays
 * out of server and proxy logs. The page reads `window.location.hash` directly, then removes the
 * fragment from the address bar and history. It stores the token with
 * {@link AuthService.applySsoAccessToken}, reloads the theme and site title as the login page does
 * (`LoginComponent.navigateAfterSignIn`), and goes to the dashboard.
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

  /** Whether the page loaded without an `access_token` in the fragment. */
  readonly failed = signal(false);

  ngOnInit(): void {
    void this.completeSignIn();
  }

  /**
   * This method reads `access_token` from the fragment, applies it and goes to the dashboard. If
   * the token is missing (a malformed or reused link, not a backend redirect), it shows the error
   * state. The refresh token never appears here; the redirect set it as a cookie.
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
