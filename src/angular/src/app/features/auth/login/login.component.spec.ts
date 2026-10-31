import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { LoginComponent } from './login.component';
import { AuthService } from '../../../core/auth/auth.service';
import { BrowserNavigationService } from '../../../core/browser/browser-navigation.service';
import {
  AppearanceSettingsService as AppearanceApiService,
  AuthService as AuthApiService,
  BrandingSettingsService as BrandingApiService,
  SsoService as SsoApiService,
} from '../../../api-client';

/**
 * A fake for the generated `SsoService`. `apiAuthSsoGet` reports SSO as unavailable by default,
 * as on a server without SSO configured; the SSO button tests override it.
 */
interface FakeSsoApiService {
  apiAuthSsoGet: jasmine.Spy;
}

/**
 * A fake for the `AppearanceApiService` that `ThemeService` uses (a plain spy object; the
 * `AuthService` spec explains why). `apiAppearanceSettingsMeGet` rejects by default, as it does
 * before sign-in; tests of the post-sign-in `ThemeService.initialize()` call override it.
 */
interface FakeAppearanceApiService {
  apiAppearanceSettingsMeGet: jasmine.Spy;
  apiAppearanceSettingsMePatch: jasmine.Spy;
}

/**
 * A fake for the `BrandingApiService` that `BrandingService` uses. It rejects by default, as before
 * sign-in; tests of the post-sign-in `BrandingService.initialize()` call override it.
 */
interface FakeBrandingApiService {
  apiBrandingSettingsEffectiveGet: jasmine.Spy;
}

/**
 * These tests cover the login page: the credentials and two-factor steps (each provider, errors,
 * cancel), "Remember me", the SSO button and error banner, and the theme, branding and
 * `returnUrl` handling after sign-in. TestBed with a fake `AuthService` and
 * `BrowserNavigationService`, the real `ThemeService` and `BrandingService` over fake API
 * services, and a stubbed route.
 */
describe('LoginComponent', () => {
  let authServiceFake: {
    login: jasmine.Spy;
    completeTwoFactorLogin: jasmine.Spy;
    takeSignInNotice: jasmine.Spy;
  };
  let appearanceApiSpy: FakeAppearanceApiService;
  let brandingApiSpy: FakeBrandingApiService;
  let ssoApiSpy: FakeSsoApiService;
  let authApiSpy: { apiAuthLoginTwoFactorEmailCodePost: jasmine.Spy };
  let browserNavigationSpy: { navigateFullPage: jasmine.Spy };
  let navigateByUrlSpy: jasmine.Spy;

  async function createComponent(
    returnUrl: string | null = null,
    queryParams: Record<string, string> = {},
    configureSso?: (spy: FakeSsoApiService) => void,
    signInNotice: string | null = null,
  ) {
    authServiceFake = {
      login: jasmine.createSpy('login'),
      completeTwoFactorLogin: jasmine.createSpy('completeTwoFactorLogin'),
      takeSignInNotice: jasmine.createSpy('takeSignInNotice').and.returnValue(signInNotice),
    };
    appearanceApiSpy = {
      apiAppearanceSettingsMeGet: jasmine
        .createSpy('apiAppearanceSettingsMeGet')
        .and.returnValue(throwError(() => new Error('unauthorized'))),
      apiAppearanceSettingsMePatch: jasmine.createSpy('apiAppearanceSettingsMePatch'),
    };
    brandingApiSpy = {
      apiBrandingSettingsEffectiveGet: jasmine
        .createSpy('apiBrandingSettingsEffectiveGet')
        .and.returnValue(throwError(() => new Error('unauthorized'))),
    };
    ssoApiSpy = {
      apiAuthSsoGet: jasmine
        .createSpy('apiAuthSsoGet')
        .and.returnValue(of({ enabled: false, displayName: 'Single Sign-On' })),
    };
    authApiSpy = {
      apiAuthLoginTwoFactorEmailCodePost: jasmine
        .createSpy('apiAuthLoginTwoFactorEmailCodePost')
        .and.returnValue(of(undefined)),
    };
    configureSso?.(ssoApiSpy);
    browserNavigationSpy = { navigateFullPage: jasmine.createSpy('navigateFullPage') };

    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authServiceFake },
        { provide: AppearanceApiService, useValue: appearanceApiSpy },
        { provide: BrandingApiService, useValue: brandingApiSpy },
        { provide: SsoApiService, useValue: ssoApiSpy },
        { provide: AuthApiService, useValue: authApiSpy },
        { provide: BrowserNavigationService, useValue: browserNavigationSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              queryParamMap: convertToParamMap({
                ...(returnUrl ? { returnUrl } : {}),
                ...queryParams,
              }),
            },
          },
        },
      ],
    }).compileComponents();

    navigateByUrlSpy = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);

    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    // ngOnInit starts loadSsoStatus() without awaiting it, and with zoneless change detection
    // fixture.whenStable() does not wait for it, so flush the task queue instead.
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    return fixture;
  }

  it('renders the credentials step using real CoreUI markup, not Angular Material', async () => {
    const fixture = await createComponent();
    const root = fixture.nativeElement as HTMLElement;

    // The card, form, inputs and button are CoreUI components.
    expect(root.querySelector('c-card')).not.toBeNull();
    expect(root.querySelector('form[cForm]')).not.toBeNull();
    expect(root.querySelectorAll('input[cFormControl]').length).toBe(2);
    expect(root.querySelector('button[cButton]')).not.toBeNull();
    expect(root.querySelector('mat-card')).toBeNull();
    expect(root.querySelector('mat-form-field')).toBeNull();
  });

  it('renders a "Forgot your password?" link to /forgot-password on the credentials step', async () => {
    const fixture = await createComponent();
    const root = fixture.nativeElement as HTMLElement;

    const link = root.querySelector('a[routerLink="/forgot-password"]');
    expect(link).not.toBeNull();
    expect(link?.textContent).toContain('Forgot your password?');
  });

  it('re-renders a CoreUI c-alert through the zoneless scheduler when errorMessage() changes', async () => {
    const fixture = await createComponent();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('c-alert')).toBeNull();

    authServiceFake.login.and.rejectWith(new HttpErrorResponse({ status: 401 }));
    fixture.componentInstance.credentialsForm.patchValue({
      userName: 'someone',
      password: 'wrong-password',
    });
    await fixture.componentInstance.submitCredentials();
    fixture.detectChanges();

    // The app is zoneless: setting the `errorMessage` signal is what schedules the re-render.
    const alert = root.querySelector('c-alert');
    expect(alert).not.toBeNull();
    expect(alert?.textContent).toContain('Incorrect username or password.');
  });

  /** Submits the credentials step with `login` rejecting with `error`, and returns the message shown. */
  async function messageAfterLoginFails(error: unknown): Promise<string | null> {
    const fixture = await createComponent();
    authServiceFake.login.and.rejectWith(error);
    fixture.componentInstance.credentialsForm.patchValue({ userName: 'someone', password: 'pw' });
    await fixture.componentInstance.submitCredentials();
    return fixture.componentInstance.errorMessage();
  }

  it('says the account is locked when sign-in fails with 423', async () => {
    expect(await messageAfterLoginFails(new HttpErrorResponse({ status: 423 }))).toBe(
      'This account is locked. Try again later, or ask an administrator to unlock it.',
    );
  });

  it('words the Retry-After wait when sign-in fails with 429', async () => {
    const error = new HttpErrorResponse({
      status: 429,
      headers: new HttpHeaders({ 'Retry-After': '120' }),
    });
    expect(await messageAfterLoginFails(error)).toBe(
      'Too many sign-in attempts. Try again in 2 minutes.',
    );
  });

  it('says the server is unreachable when sign-in fails with status 0', async () => {
    expect(await messageAfterLoginFails(new HttpErrorResponse({ status: 0 }))).toBe(
      'Could not reach the server. Check your connection and try again.',
    );
  });

  it('names the status when sign-in fails with a server error', async () => {
    expect(await messageAfterLoginFails(new HttpErrorResponse({ status: 500 }))).toBe(
      'Sign-in failed because of a server error (HTTP 500). Try again.',
    );
  });

  it('says the password is wrong when sign-in fails with 401', async () => {
    expect(await messageAfterLoginFails(new HttpErrorResponse({ status: 401 }))).toBe(
      'Incorrect username or password.',
    );
  });

  it('does nothing when the credentials form is submitted while invalid', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.submitCredentials();

    expect(authServiceFake.login).not.toHaveBeenCalled();
  });

  it('navigates to /dashboard after a successful sign-in with no second factor', async () => {
    const fixture = await createComponent();
    authServiceFake.login.and.resolveTo({ requiresTwoFactor: false });

    fixture.componentInstance.credentialsForm.patchValue({
      userName: 'someone',
      password: 'correct-password',
    });
    await fixture.componentInstance.submitCredentials();

    expect(authServiceFake.login).toHaveBeenCalledWith('someone', 'correct-password', false);
    expect(navigateByUrlSpy).toHaveBeenCalledWith('/dashboard');
  });

  it("re-runs ThemeService.initialize() after a successful sign-in, to pick up the caller's own persisted appearance preference", async () => {
    const fixture = await createComponent();
    authServiceFake.login.and.resolveTo({ requiresTwoFactor: false });
    appearanceApiSpy.apiAppearanceSettingsMeGet.and.returnValue(
      of({ palette: 'Rose', mode: 'Dark', effectivePalette: 'Rose', effectiveMode: 'Dark' }),
    );

    fixture.componentInstance.credentialsForm.patchValue({
      userName: 'someone',
      password: 'correct-password',
    });
    await fixture.componentInstance.submitCredentials();

    // The startup call ran without a session and failed. This call must happen before
    // navigating, so the next page does not render in the fallback theme.
    expect(appearanceApiSpy.apiAppearanceSettingsMeGet).toHaveBeenCalledTimes(1);
    expect(navigateByUrlSpy).toHaveBeenCalledWith('/dashboard');
  });

  it('re-runs BrandingService.initialize() after a successful sign-in, applying a real configured site title to document.title before navigating away', async () => {
    const originalTitle = document.title;
    try {
      const fixture = await createComponent();
      authServiceFake.login.and.resolveTo({ requiresTwoFactor: false });
      brandingApiSpy.apiBrandingSettingsEffectiveGet.and.returnValue(
        of({ siteTitle: 'Acme Corp DNS', logoDataUri: null }),
      );

      fixture.componentInstance.credentialsForm.patchValue({
        userName: 'someone',
        password: 'correct-password',
      });
      await fixture.componentInstance.submitCredentials();

      // The startup call ran without a session and fell back to the default title. Check that
      // document.title shows the configured value, not only that the API was called.
      expect(brandingApiSpy.apiBrandingSettingsEffectiveGet).toHaveBeenCalledTimes(1);
      expect(document.title).toBe('Acme Corp DNS');
      expect(navigateByUrlSpy).toHaveBeenCalledWith('/dashboard');
    } finally {
      document.title = originalTitle;
    }
  });

  it('navigates back to the preserved returnUrl when one was set by authGuard', async () => {
    const fixture = await createComponent('/zones');
    authServiceFake.login.and.resolveTo({ requiresTwoFactor: false });

    fixture.componentInstance.credentialsForm.patchValue({
      userName: 'someone',
      password: 'correct-password',
    });
    await fixture.componentInstance.submitCredentials();

    expect(navigateByUrlSpy).toHaveBeenCalledWith('/zones');
  });

  it('switches to the two-factor step, without navigating, when a second factor is required', async () => {
    const fixture = await createComponent();
    authServiceFake.login.and.resolveTo({ requiresTwoFactor: true, mfaToken: 'pending-token' });

    fixture.componentInstance.credentialsForm.patchValue({
      userName: 'someone',
      password: 'correct-password',
    });
    await fixture.componentInstance.submitCredentials();

    expect(fixture.componentInstance.step()).toBe('two-factor');
    expect(navigateByUrlSpy).not.toHaveBeenCalled();
  });

  it('renders the two-factor step using CoreUI form-check radios, not mat-radio', async () => {
    const fixture = await createComponent();
    const root = fixture.nativeElement as HTMLElement;
    authServiceFake.login.and.resolveTo({ requiresTwoFactor: true, mfaToken: 'pending-token' });

    fixture.componentInstance.credentialsForm.patchValue({
      userName: 'someone',
      password: 'correct-password',
    });
    await fixture.componentInstance.submitCredentials();
    fixture.detectChanges();

    expect(root.querySelectorAll('c-form-check').length).toBe(3);
    expect(root.querySelectorAll('input[cFormCheckInput][type="radio"]').length).toBe(3);
    expect(root.querySelector('mat-radio-group')).toBeNull();
  });

  it('shows an error message and stays on the credentials step when sign-in fails', async () => {
    const fixture = await createComponent();
    authServiceFake.login.and.rejectWith(new HttpErrorResponse({ status: 401 }));

    fixture.componentInstance.credentialsForm.patchValue({
      userName: 'someone',
      password: 'wrong-password',
    });
    await fixture.componentInstance.submitCredentials();

    expect(fixture.componentInstance.step()).toBe('credentials');
    expect(fixture.componentInstance.errorMessage()).not.toBeNull();
  });

  it('completes sign-in once a valid two-factor code is submitted', async () => {
    const fixture = await createComponent();
    authServiceFake.login.and.resolveTo({ requiresTwoFactor: true, mfaToken: 'pending-token' });
    fixture.componentInstance.credentialsForm.patchValue({
      userName: 'someone',
      password: 'correct-password',
    });
    await fixture.componentInstance.submitCredentials();

    authServiceFake.completeTwoFactorLogin.and.resolveTo(undefined);
    fixture.componentInstance.twoFactorForm.setValue({
      code: '123456',
      provider: 'Authenticator',
    });
    await fixture.componentInstance.submitTwoFactorCode();

    expect(authServiceFake.completeTwoFactorLogin).toHaveBeenCalledWith(
      'pending-token',
      '123456',
      'Authenticator',
    );
    expect(navigateByUrlSpy).toHaveBeenCalledWith('/dashboard');
  });

  it('requests an e-mailed code with only the pending sign-in token', async () => {
    const fixture = await createComponent();
    authServiceFake.login.and.resolveTo({ requiresTwoFactor: true, mfaToken: 'pending-token' });
    fixture.componentInstance.credentialsForm.patchValue({
      userName: 'someone',
      password: 'correct-password',
    });
    await fixture.componentInstance.submitCredentials();

    await fixture.componentInstance.sendEmailCode();

    expect(authApiSpy.apiAuthLoginTwoFactorEmailCodePost).toHaveBeenCalledWith({
      twoFactorEmailCodeRequest: { mfaToken: 'pending-token' },
    });
    expect(fixture.componentInstance.emailCodeState()).toBe('sent');
  });

  it('shows an error when the e-mailed code cannot be requested', async () => {
    const fixture = await createComponent();
    authServiceFake.login.and.resolveTo({ requiresTwoFactor: true, mfaToken: 'pending-token' });
    fixture.componentInstance.credentialsForm.patchValue({
      userName: 'someone',
      password: 'correct-password',
    });
    await fixture.componentInstance.submitCredentials();
    authApiSpy.apiAuthLoginTwoFactorEmailCodePost.and.returnValue(
      throwError(() => new Error('bad request')),
    );

    await fixture.componentInstance.sendEmailCode();

    expect(fixture.componentInstance.emailCodeState()).toBe('idle');
    expect(fixture.componentInstance.errorMessage()).toContain('could not be sent');
  });

  it('tells the user to wait when the e-mailed code was requested too soon', async () => {
    const fixture = await createComponent();
    authServiceFake.login.and.resolveTo({ requiresTwoFactor: true, mfaToken: 'pending-token' });
    fixture.componentInstance.credentialsForm.patchValue({
      userName: 'someone',
      password: 'correct-password',
    });
    await fixture.componentInstance.submitCredentials();
    authApiSpy.apiAuthLoginTwoFactorEmailCodePost.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 429 })),
    );

    await fixture.componentInstance.sendEmailCode();

    expect(fixture.componentInstance.emailCodeState()).toBe('idle');
    expect(fixture.componentInstance.errorMessage()).toContain('Wait a minute');
  });

  it('says how long to wait when the two-factor code check answers 429', async () => {
    const fixture = await createComponent();
    authServiceFake.login.and.resolveTo({ requiresTwoFactor: true, mfaToken: 'pending-token' });
    fixture.componentInstance.credentialsForm.patchValue({
      userName: 'someone',
      password: 'correct-password',
    });
    await fixture.componentInstance.submitCredentials();

    authServiceFake.completeTwoFactorLogin.and.rejectWith(
      new HttpErrorResponse({ status: 429, headers: new HttpHeaders({ 'Retry-After': '840' }) }),
    );
    fixture.componentInstance.twoFactorForm.setValue({
      code: '000000',
      provider: 'Authenticator',
    });
    await fixture.componentInstance.submitTwoFactorCode();

    expect(fixture.componentInstance.step()).toBe('two-factor');
    expect(fixture.componentInstance.errorMessage()).toBe(
      'Too many attempts. Try again in 14 minutes.',
    );
  });

  it('falls back to a general wait when the 429 has no Retry-After header', async () => {
    const fixture = await createComponent();
    authServiceFake.login.and.resolveTo({ requiresTwoFactor: true, mfaToken: 'pending-token' });
    fixture.componentInstance.credentialsForm.patchValue({
      userName: 'someone',
      password: 'correct-password',
    });
    await fixture.componentInstance.submitCredentials();

    authServiceFake.completeTwoFactorLogin.and.rejectWith(new HttpErrorResponse({ status: 429 }));
    fixture.componentInstance.twoFactorForm.setValue({
      code: '000000',
      provider: 'Authenticator',
    });
    await fixture.componentInstance.submitTwoFactorCode();

    expect(fixture.componentInstance.errorMessage()).toBe(
      'Too many attempts. Try again in a few minutes.',
    );
  });

  it('shows an error and stays on the two-factor step when the code is rejected', async () => {
    const fixture = await createComponent();
    authServiceFake.login.and.resolveTo({ requiresTwoFactor: true, mfaToken: 'pending-token' });
    fixture.componentInstance.credentialsForm.patchValue({
      userName: 'someone',
      password: 'correct-password',
    });
    await fixture.componentInstance.submitCredentials();

    authServiceFake.completeTwoFactorLogin.and.rejectWith(new Error('invalid code'));
    fixture.componentInstance.twoFactorForm.setValue({
      code: '000000',
      provider: 'Authenticator',
    });
    await fixture.componentInstance.submitTwoFactorCode();

    expect(fixture.componentInstance.step()).toBe('two-factor');
    expect(fixture.componentInstance.errorMessage()).not.toBeNull();
  });

  it('completes sign-in with a recovery code instead of an authenticator/e-mailed code', async () => {
    const fixture = await createComponent();
    authServiceFake.login.and.resolveTo({ requiresTwoFactor: true, mfaToken: 'pending-token' });
    fixture.componentInstance.credentialsForm.patchValue({
      userName: 'someone',
      password: 'correct-password',
    });
    await fixture.componentInstance.submitCredentials();

    authServiceFake.completeTwoFactorLogin.and.resolveTo(undefined);
    fixture.componentInstance.twoFactorForm.setValue({
      code: 'ABCD1234',
      provider: 'RecoveryCode',
    });
    await fixture.componentInstance.submitTwoFactorCode();

    expect(authServiceFake.completeTwoFactorLogin).toHaveBeenCalledWith(
      'pending-token',
      'ABCD1234',
      'RecoveryCode',
    );
    expect(navigateByUrlSpy).toHaveBeenCalledWith('/dashboard');
  });

  it('shows an error and stays on the two-factor step when a recovery code is rejected', async () => {
    const fixture = await createComponent();
    authServiceFake.login.and.resolveTo({ requiresTwoFactor: true, mfaToken: 'pending-token' });
    fixture.componentInstance.credentialsForm.patchValue({
      userName: 'someone',
      password: 'correct-password',
    });
    await fixture.componentInstance.submitCredentials();

    authServiceFake.completeTwoFactorLogin.and.rejectWith(new Error('already redeemed'));
    fixture.componentInstance.twoFactorForm.setValue({
      code: 'ALREADY-USED',
      provider: 'RecoveryCode',
    });
    await fixture.componentInstance.submitTwoFactorCode();

    expect(fixture.componentInstance.step()).toBe('two-factor');
    expect(fixture.componentInstance.errorMessage()).not.toBeNull();
  });

  it('cancelTwoFactor() returns to the credentials step and abandons the pending challenge', async () => {
    const fixture = await createComponent();
    authServiceFake.login.and.resolveTo({ requiresTwoFactor: true, mfaToken: 'pending-token' });
    fixture.componentInstance.credentialsForm.patchValue({
      userName: 'someone',
      password: 'correct-password',
    });
    await fixture.componentInstance.submitCredentials();

    fixture.componentInstance.cancelTwoFactor();
    expect(fixture.componentInstance.step()).toBe('credentials');

    fixture.componentInstance.twoFactorForm.setValue({
      code: '123456',
      provider: 'Authenticator',
    });
    await fixture.componentInstance.submitTwoFactorCode();

    // The pending mfaToken was cleared by cancelTwoFactor(), so this must be a no-op.
    expect(authServiceFake.completeTwoFactorLogin).not.toHaveBeenCalled();
  });

  it('does not render a "Sign in with SSO" button when GET /api/auth/sso reports it disabled', async () => {
    const fixture = await createComponent();
    const root = fixture.nativeElement as HTMLElement;

    expect(ssoApiSpy.apiAuthSsoGet).toHaveBeenCalledTimes(1);
    expect(root.querySelector('.login-sso-divider')).toBeNull();
  });

  it('renders a "Sign in with SSO" button, labeled from the backend, when GET /api/auth/sso reports it enabled', async () => {
    const fixture = await createComponent(null, {}, (spy) => {
      spy.apiAuthSsoGet.and.returnValue(
        of({ enabled: true, displayName: 'Sign in with Authelia' }),
      );
    });
    const root = fixture.nativeElement as HTMLElement;

    expect(fixture.componentInstance.ssoEnabled()).toBe(true);
    const buttons = Array.from(root.querySelectorAll('button[cButton]'));
    const ssoButton = buttons.find((button) =>
      button.textContent?.includes('Sign in with Authelia'),
    );
    expect(ssoButton).toBeTruthy();
  });

  it('treats a failed GET /api/auth/sso the same as "disabled", never showing an error for it', async () => {
    const fixture = await createComponent(null, {}, (spy) => {
      spy.apiAuthSsoGet.and.returnValue(throwError(() => new Error('network error')));
    });

    expect(fixture.componentInstance.ssoEnabled()).toBe(false);
    expect(fixture.componentInstance.errorMessage()).toBeNull();
  });

  it('startSsoSignIn() performs a real, full-page navigation to /api/auth/sso/challenge, not a router call', async () => {
    const fixture = await createComponent(null, {}, (spy) => {
      spy.apiAuthSsoGet.and.returnValue(of({ enabled: true, displayName: 'Single Sign-On' }));
    });

    fixture.componentInstance.startSsoSignIn();

    expect(browserNavigationSpy.navigateFullPage).toHaveBeenCalledWith(
      '/api/auth/sso/challenge?rememberMe=false',
    );
    expect(navigateByUrlSpy).not.toHaveBeenCalled();
  });

  it('passes a checked "Remember me" to both a password and an SSO sign-in', async () => {
    const fixture = await createComponent(null, {}, (spy) => {
      spy.apiAuthSsoGet.and.returnValue(of({ enabled: true, displayName: 'Single Sign-On' }));
    });
    authServiceFake.login.and.resolveTo({ requiresTwoFactor: false });

    const checkbox: HTMLInputElement = fixture.nativeElement.querySelector(
      '[data-testid="remember-me"]',
    );
    checkbox.click();
    fixture.componentInstance.credentialsForm.patchValue({
      userName: 'someone',
      password: 'correct-password',
    });

    fixture.componentInstance.startSsoSignIn();
    await fixture.componentInstance.submitCredentials();

    expect(browserNavigationSpy.navigateFullPage).toHaveBeenCalledWith(
      '/api/auth/sso/challenge?rememberMe=true',
    );
    expect(authServiceFake.login).toHaveBeenCalledWith('someone', 'correct-password', true);
  });

  it('shows an inline banner when this page loaded with ?ssoError=1', async () => {
    const fixture = await createComponent(null, { ssoError: '1' });
    const root = fixture.nativeElement as HTMLElement;

    expect(fixture.componentInstance.ssoError()).toBe(true);
    const alert = root.querySelector('c-alert');
    expect(alert).not.toBeNull();
    expect(alert?.textContent).toContain('Single sign-on could not complete');
  });

  it('shows why the session ended after a password or two-factor change', async () => {
    const fixture = await createComponent(
      null,
      {},
      undefined,
      'Your password was changed and every session of your account was signed out.',
    );
    const root = fixture.nativeElement as HTMLElement;

    expect(fixture.componentInstance.signInNotice()).toContain('Your password was changed');
    expect(root.querySelector('c-alert')?.textContent).toContain('Your password was changed');
  });

  it('shows no notice on an ordinary visit', async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.signInNotice()).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('c-alert')).toBeNull();
  });

  it('does not show the ssoError banner on an ordinary visit with no query params', async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.ssoError()).toBe(false);
  });
});
