import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { LoginComponent } from './login.component';
import { AuthService } from '../../../core/auth/auth.service';
import { BrowserNavigationService } from '../../../core/browser/browser-navigation.service';
import {
  AppearanceSettingsService as AppearanceApiService,
  BrandingSettingsService as BrandingApiService,
  SsoService as SsoApiService,
} from '../../../api-client';

/**
 * A fake for {@link SsoService}'s own generated `SsoApiService` dependency, the same shape as
 * {@link FakeAppearanceApiService}/{@link FakeBrandingApiService} below: `apiAuthSsoGet` defaults
 * to reporting SSO as unavailable (`enabled: false`), matching every deployment that hasn't
 * configured `Identity:Sso` at all; individual tests below override it to verify this
 * component's own SSO button.
 */
interface FakeSsoApiService {
  apiAuthSsoGet: jasmine.Spy;
}

/**
 * A fake for `ThemeService`'s own `AppearanceApiService` dependency -- see
 * `ThemeSwitcherComponent`'s own spec for why a plain Jasmine-spy fake is used here instead of
 * `jasmine.SpyObj`. `apiAppearanceSettingsMeGet` defaults to rejecting (the real, anonymous-
 * caller shape `GET /me` returns pre-sign-in, a 401), matching production; individual tests below
 * override it to verify {@link LoginComponent}'s own post-sign-in `ThemeService.initialize()` call.
 */
interface FakeAppearanceApiService {
  apiAppearanceSettingsMeGet: jasmine.Spy;
  apiAppearanceSettingsMePatch: jasmine.Spy;
}

/**
 * A fake for `BrandingService`'s own `BrandingApiService` dependency, the same shape as
 * {@link FakeAppearanceApiService} and for the same reason: `apiBrandingSettingsEffectiveGet`
 * defaults to rejecting (the real, anonymous-caller pre-sign-in shape), and individual tests below
 * override it to verify {@link LoginComponent}'s own post-sign-in `BrandingService.initialize()`
 * call.
 */
interface FakeBrandingApiService {
  apiBrandingSettingsEffectiveGet: jasmine.Spy;
}

describe('LoginComponent', () => {
  let authServiceFake: { login: jasmine.Spy; completeTwoFactorLogin: jasmine.Spy };
  let appearanceApiSpy: FakeAppearanceApiService;
  let brandingApiSpy: FakeBrandingApiService;
  let ssoApiSpy: FakeSsoApiService;
  let browserNavigationSpy: { navigateFullPage: jasmine.Spy };
  let navigateByUrlSpy: jasmine.Spy;

  async function createComponent(
    returnUrl: string | null = null,
    queryParams: Record<string, string> = {},
    configureSso?: (spy: FakeSsoApiService) => void,
  ) {
    authServiceFake = {
      login: jasmine.createSpy('login'),
      completeTwoFactorLogin: jasmine.createSpy('completeTwoFactorLogin'),
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
    // ngOnInit fires this component's own loadSsoStatus() as a fire-and-forget promise chain
    // (void this.loadSsoStatus()), not something this test awaits directly -- under this app's
    // zoneless change detection, fixture.whenStable() does not reliably wait for it, so a real
    // microtask-queue flush is used instead, the same helper SmtpSettingsComponent's own spec
    // already establishes for an identical reason.
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    return fixture;
  }

  it('renders the credentials step using real CoreUI markup, not Angular Material', async () => {
    const fixture = await createComponent();
    const root = fixture.nativeElement as HTMLElement;

    // A genuine like-for-like swap: the card/form/input/button structure is now CoreUI's own
    // components (`c-card`, `cForm`, `cFormControl`, `cButton`), and no Material component
    // selector should remain anywhere in the rendered tree.
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

    authServiceFake.login.and.rejectWith(new Error('unauthorized'));
    fixture.componentInstance.credentialsForm.patchValue({
      userName: 'someone',
      password: 'wrong-password',
    });
    await fixture.componentInstance.submitCredentials();
    fixture.detectChanges();

    // No zone.js is loaded in this app (zoneless change detection) -- this assertion only
    // passes because Angular's signal-based reactivity itself schedules the re-render once
    // `errorMessage` is set, the same mechanism this app already relies on everywhere else.
    const alert = root.querySelector('c-alert');
    expect(alert).not.toBeNull();
    expect(alert?.textContent).toContain('Incorrect username or password.');
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

    // The app-startup call (before this component even existed) ran anonymously and always
    // fails; this is the one call a real session makes, and it must happen before navigating
    // away so the next page never renders in the stale, pre-sign-in fallback theme.
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

      // The app-startup call ran anonymously and always fails (BrandingService.initialize()
      // catches that and falls back to the compiled-in title); this is the one call a real
      // session makes, and document.title genuinely reflects the real configured value
      // afterward -- not merely that the API was called.
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
    authServiceFake.login.and.rejectWith(new Error('unauthorized'));

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

  it('does not show the ssoError banner on an ordinary visit with no query params', async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.ssoError()).toBe(false);
  });
});
