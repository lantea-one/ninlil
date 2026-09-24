import { Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardHeaderComponent,
  FormCheckComponent,
  FormCheckInputDirective,
  FormCheckLabelDirective,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import { SsoService } from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { BrandingService } from '../../../core/branding/branding.service';
import { BrowserNavigationService } from '../../../core/browser/browser-navigation.service';
import { ThemeService } from '../../../core/theme/theme.service';

/**
 * This component covers the whole sign-in flow in one page, switching between two steps rather
 * than two separate routes: submitting a username/password (`credentials`), and, only for an
 * account with two-factor authentication enabled, submitting the second-factor code
 * (`two-factor`). A single component keeps the pending sign-in's own `mfaToken` (returned by
 * `POST /api/auth/login`) as plain component state, with no need to round-trip it through a
 * route parameter.
 *
 * The two-factor step's own provider radio offers a third option, `"RecoveryCode"`, alongside
 * the `"Authenticator"`/`"Email"` pair -- a fallback for an operator who has lost access to both
 * (see `AccountSecurityComponent` for where a recovery code batch is generated). The code text
 * field is reused as-is for all three; a recovery code is redeemed
 * (single-use) rather than merely verified, which `AuthController.TwoFactorLogin` itself already
 * handles transparently based on this same `provider` value.
 *
 * <p>This page also renders an optional "Sign in with SSO" button, driven by `GET /api/auth/sso`
 * (`SsoController.Get`) -- the honest, live "is the OpenID Connect scheme actually registered on
 * this running process right now" signal, not merely `OidcSsoOptions.Enable`'s own raw
 * configuration value (see that action's own doc comment for why those two can genuinely
 * disagree). Clicking it is a real, full-page navigation to
 * `/api/auth/sso/challenge` (`window.location.href`, never a router link) -- it must leave this
 * Angular app entirely and hit the server-side redirect to the identity provider; there is
 * nothing for the Angular router to navigate to here. `SsoCallbackComponent` is where the
 * browser lands once that whole round trip completes. A failed/cancelled identity-provider flow
 * instead redirects back here with `?ssoError=1`, shown as an inline banner.
 */
@Component({
  selector: 'app-login',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardHeaderComponent,
    FormCheckComponent,
    FormCheckInputDirective,
    FormCheckLabelDirective,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    IconDirective,
    SpinnerComponent,
  ],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly formBuilder = inject(FormBuilder);
  private readonly themeService = inject(ThemeService);
  private readonly brandingService = inject(BrandingService);
  private readonly ssoApi = inject(SsoService);
  private readonly browserNavigation = inject(BrowserNavigationService);

  /** Which step of the sign-in flow is currently shown. */
  readonly step = signal<'credentials' | 'two-factor'>('credentials');

  /** Whether a login/refresh/verify network call is currently in flight. */
  readonly submitting = signal(false);

  /** The most recent error message to show, or `null` when there isn't one. */
  readonly errorMessage = signal<string | null>(null);

  /** Whether this running process currently has a working OIDC scheme to challenge against -- see this class's own doc comment. */
  readonly ssoEnabled = signal(false);

  /** The label to render on the "Sign in with SSO" button, once {@link ssoEnabled} is `true`. */
  readonly ssoDisplayName = signal('Single Sign-On');

  /** Whether this page loaded with `?ssoError=1` -- a failed/cancelled identity-provider flow bounced back here. */
  readonly ssoError = signal(false);

  /** The opaque token identifying a pending two-factor sign-in, once one exists. */
  private mfaToken: string | null = null;

  ngOnInit(): void {
    this.ssoError.set(this.route.snapshot.queryParamMap.get('ssoError') !== null);
    void this.loadSsoStatus();
  }

  /**
   * This method starts the OIDC challenge with a real, full-page browser navigation -- not a
   * `fetch()`/router call -- so the browser genuinely leaves this Angular app and follows the
   * server's own redirect chain all the way to the identity provider. See this class's own doc
   * comment for why a router link would be wrong here. Delegates to
   * {@link BrowserNavigationService} rather than touching `window.location` directly -- see that
   * service's own doc comment for why.
   */
  startSsoSignIn(): void {
    // The "Remember me" choice rides along, so an SSO sign-in gets the same session lifetime a
    // password sign-in would.
    const rememberMe = this.credentialsForm.controls.rememberMe.value;
    this.browserNavigation.navigateFullPage(`/api/auth/sso/challenge?rememberMe=${rememberMe}`);
  }

  /**
   * This method loads the current "is SSO available at all" status. A network failure here is
   * treated the same as `enabled: false` -- there is no meaningful error state for this page's
   * own optional SSO button, only "show it" or "don't."
   */
  private async loadSsoStatus(): Promise<void> {
    try {
      const status = await firstValueFrom(this.ssoApi.apiAuthSsoGet());
      this.ssoEnabled.set(status.enabled ?? false);
      this.ssoDisplayName.set(status.displayName ?? 'Single Sign-On');
    } catch {
      this.ssoEnabled.set(false);
    }
  }

  readonly credentialsForm = this.formBuilder.nonNullable.group({
    userName: ['', Validators.required],
    password: ['', Validators.required],
    // Whether the session should outlive the browser -- see `AuthService`'s own doc comment.
    rememberMe: [false],
  });

  readonly twoFactorForm = this.formBuilder.nonNullable.group({
    code: ['', Validators.required],
    provider: ['Authenticator' as 'Authenticator' | 'Email' | 'RecoveryCode', Validators.required],
  });

  /** This method submits the credentials step. */
  async submitCredentials(): Promise<void> {
    if (this.credentialsForm.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      const { userName, password, rememberMe } = this.credentialsForm.getRawValue();
      const outcome = await this.authService.login(userName, password, rememberMe);

      if (outcome.requiresTwoFactor) {
        this.mfaToken = outcome.mfaToken;
        this.step.set('two-factor');
      } else {
        await this.navigateAfterSignIn();
      }
    } catch {
      this.errorMessage.set('Incorrect username or password.');
    } finally {
      this.submitting.set(false);
    }
  }

  /** This method submits the two-factor step. */
  async submitTwoFactorCode(): Promise<void> {
    if (this.twoFactorForm.invalid || this.submitting() || !this.mfaToken) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      const { code, provider } = this.twoFactorForm.getRawValue();
      await this.authService.completeTwoFactorLogin(this.mfaToken, code, provider);
      await this.navigateAfterSignIn();
    } catch {
      this.errorMessage.set('That code was not accepted. Please try again.');
    } finally {
      this.submitting.set(false);
    }
  }

  /** This method returns to the credentials step, abandoning the pending two-factor challenge. */
  cancelTwoFactor(): void {
    this.mfaToken = null;
    this.twoFactorForm.reset({ code: '', provider: 'Authenticator' });
    this.errorMessage.set(null);
    this.step.set('credentials');
  }

  /**
   * This method navigates to wherever {@link authGuard} originally redirected from, or the
   * dashboard, once a real access token already exists (`AuthService.login`/
   * `completeTwoFactorLogin` both apply their tokens synchronously before resolving). It first
   * re-runs {@link ThemeService.initialize} -- the app-startup call made before this page even
   * rendered ran anonymously (`GET /api/appearance-settings/me` always 401s pre-sign-in) and so
   * only ever applied this app's own compiled-in fallback palette/mode; re-running it now, with
   * a real session in hand, is what actually pulls in the signed-in caller's own persisted
   * preference -- without this call the rest of the session would silently stay on the fallback
   * theme until a full page reload, which itself signs the caller back out (in-memory-only
   * tokens, see `AuthService`'s own doc comment), so it would never happen in practice. It also
   * re-runs {@link BrandingService.initialize} for exactly the same reason -- the app-startup call
   * made before this page rendered ran anonymously too (`GET /api/branding-settings/effective`
   * also requires authentication), so this is what actually applies a real configured site title
   * once a session exists.
   */
  private async navigateAfterSignIn(): Promise<void> {
    await Promise.all([this.themeService.initialize(), this.brandingService.initialize()]);
    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
    await this.router.navigateByUrl(returnUrl ?? '/dashboard');
  }
}
