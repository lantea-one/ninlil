import { HttpErrorResponse } from '@angular/common/http';
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
import { AuthService as AuthApiService, SsoService } from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { BrandingService } from '../../../core/branding/branding.service';
import { BrowserNavigationService } from '../../../core/browser/browser-navigation.service';
import { ThemeService } from '../../../core/theme/theme.service';

/** This function reports whether an error is an HTTP 429 (too many requests) response. */
function isTooManyRequests(error: unknown): error is HttpErrorResponse {
  return error instanceof HttpErrorResponse && error.status === 429;
}

/**
 * This function words the wait that a 429's `Retry-After` header (in seconds) asks for, rounded
 * up to whole minutes. Without a usable header it says "a few minutes".
 */
function waitMinutes(error: HttpErrorResponse): string {
  const seconds = Number(error.headers?.get('Retry-After'));
  if (!Number.isFinite(seconds) || seconds <= 0) return 'a few minutes';

  const minutes = Math.ceil(seconds / 60);
  return `${minutes} minute${minutes === 1 ? '' : 's'}`;
}

/**
 * This component is the public login page (`/login`). It has two steps on one page:
 * `credentials` (user name, password, "Remember me") and, for an account with two-factor sign-in,
 * `two-factor`. Keeping both in one component lets the pending sign-in's `mfaToken` from
 * `POST /api/auth/login` stay in component state. Sign-in goes through {@link AuthService}.
 *
 * <p>The two-factor step accepts an authenticator code, an e-mailed code or a recovery code (a
 * fallback when the others are unavailable; the Security page generates them). One text field
 * serves all three; the backend redeems a recovery code so it cannot be used again.
 *
 * <p>A "Sign in with SSO" button appears when `GET /api/auth/sso` reports that OpenID Connect is
 * available. That endpoint checks whether the scheme was registered at startup, not the current
 * setting, so the button never leads to a 404. The button does a full-page navigation to
 * `/api/auth/sso/challenge`, because the server redirects to the identity provider. A completed
 * sign-in lands on `SsoCallbackComponent`; a failed or cancelled one returns here with
 * `?ssoError=1`, shown as a banner.
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
  private readonly authApi = inject(AuthApiService);
  private readonly browserNavigation = inject(BrowserNavigationService);

  /** Which step of the sign-in flow is currently shown. */
  readonly step = signal<'credentials' | 'two-factor'>('credentials');

  /** Whether a login/refresh/verify network call is currently in flight. */
  readonly submitting = signal(false);

  /** The most recent error message to show, or `null` when there isn't one. */
  readonly errorMessage = signal<string | null>(null);

  /** Whether SSO sign-in is available (see the class comment). */
  readonly ssoEnabled = signal(false);

  /** The label to render on the "Sign in with SSO" button, once {@link ssoEnabled} is `true`. */
  readonly ssoDisplayName = signal('Single Sign-On');

  /** Whether the page loaded with `?ssoError=1` after a failed or cancelled SSO sign-in. */
  readonly ssoError = signal(false);

  /** The opaque token identifying a pending two-factor sign-in, once one exists. */
  private mfaToken: string | null = null;

  ngOnInit(): void {
    this.ssoError.set(this.route.snapshot.queryParamMap.get('ssoError') !== null);
    void this.loadSsoStatus();
  }

  /**
   * This method starts SSO sign-in with a full-page navigation to `/api/auth/sso/challenge`,
   * which redirects to the identity provider. It goes through {@link BrowserNavigationService} so
   * tests can fake it.
   */
  startSsoSignIn(): void {
    // The "Remember me" choice rides along, so an SSO sign-in gets the same session lifetime a
    // password sign-in would.
    const rememberMe = this.credentialsForm.controls.rememberMe.value;
    this.browserNavigation.navigateFullPage(`/api/auth/sso/challenge?rememberMe=${rememberMe}`);
  }

  /**
   * This method asks whether SSO is available. A failure hides the button, since there is no
   * useful error to show for an optional button.
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
    // Whether the session outlives the browser; see AuthService.
    rememberMe: [false],
  });

  readonly twoFactorForm = this.formBuilder.nonNullable.group({
    code: ['', Validators.required],
    provider: ['Authenticator' as 'Authenticator' | 'Email' | 'RecoveryCode', Validators.required],
  });

  /**
   * This method submits the credentials step. Any failure shows "Incorrect username or
   * password.", including a locked account (423) or a network error.
   */
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
    } catch (error) {
      this.errorMessage.set(
        isTooManyRequests(error)
          ? `Too many attempts. Try again in ${waitMinutes(error)}.`
          : 'That code was not accepted. Please try again.',
      );
    } finally {
      this.submitting.set(false);
    }
  }

  /** Whether an e-mailed code is being requested, and whether the last request succeeded. */
  readonly emailCodeState = signal<'idle' | 'sending' | 'sent'>('idle');

  /**
   * This method asks the server to e-mail a verification code for the pending sign-in
   * (`POST /api/auth/login/two-factor/email-code`). The pending sign-in's `mfaToken` authorizes
   * it, since there is no session yet.
   */
  async sendEmailCode(): Promise<void> {
    if (!this.mfaToken || this.emailCodeState() === 'sending') return;

    this.emailCodeState.set('sending');
    this.errorMessage.set(null);

    try {
      await firstValueFrom(
        this.authApi.apiAuthLoginTwoFactorEmailCodePost({
          twoFactorEmailCodeRequest: { mfaToken: this.mfaToken },
        }),
      );
      this.emailCodeState.set('sent');
    } catch (error) {
      this.emailCodeState.set('idle');
      this.errorMessage.set(
        isTooManyRequests(error)
          ? 'A code was sent recently. Wait a minute before asking for another.'
          : 'A code could not be sent. The account may have no confirmed e-mail address.',
      );
    }
  }

  /** This method returns to the credentials step, abandoning the pending two-factor challenge. */
  cancelTwoFactor(): void {
    this.mfaToken = null;
    this.emailCodeState.set('idle');
    this.twoFactorForm.reset({ code: '', provider: 'Authenticator' });
    this.errorMessage.set(null);
    this.step.set('credentials');
  }

  /**
   * This method runs after a successful sign-in. It reloads the user's theme
   * ({@link ThemeService.initialize}) and the site title ({@link BrandingService.initialize}),
   * since the startup calls ran without a session and fell back to the defaults. It then goes to
   * the `returnUrl` that {@link authGuard} added, or to the dashboard.
   */
  private async navigateAfterSignIn(): Promise<void> {
    await Promise.all([this.themeService.initialize(), this.brandingService.initialize()]);
    const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
    await this.router.navigateByUrl(returnUrl ?? '/dashboard');
  }
}
