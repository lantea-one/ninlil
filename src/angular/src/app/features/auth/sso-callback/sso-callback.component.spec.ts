import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { SsoCallbackComponent } from './sso-callback.component';
import {
  AppearanceSettingsService as AppearanceApiService,
  BrandingSettingsService as BrandingApiService,
} from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';

/**
 * A fake for the `AppearanceApiService` that `ThemeService` uses, as in the login spec. It rejects
 * by default, as before sign-in; tests override it.
 */
interface FakeAppearanceApiService {
  apiAppearanceSettingsMeGet: jasmine.Spy;
  apiAppearanceSettingsMePatch: jasmine.Spy;
}

/** A fake for the `BrandingApiService` that `BrandingService` uses; see {@link FakeAppearanceApiService}. */
interface FakeBrandingApiService {
  apiBrandingSettingsEffectiveGet: jasmine.Spy;
}

/**
 * These tests cover the SSO callback: applying the token from the fragment, clearing it from the
 * address bar, reloading theme and branding, navigating, and the error state without a token.
 * TestBed with a fake `AuthService` and the real `ThemeService` and `BrandingService` over fake
 * APIs; tests set `window.location.hash`.
 */
describe('SsoCallbackComponent', () => {
  let authServiceFake: { applySsoAccessToken: jasmine.Spy };
  let appearanceApiSpy: FakeAppearanceApiService;
  let brandingApiSpy: FakeBrandingApiService;
  let navigateByUrlSpy: jasmine.Spy;
  const originalHash = window.location.hash;

  afterEach(() => {
    // window.location.hash can be set in a test (it does not navigate away), but it must be
    // cleared so it does not leak into the next test.
    window.location.hash = originalHash;
  });

  async function createComponent(hash: string) {
    window.location.hash = hash;

    authServiceFake = { applySsoAccessToken: jasmine.createSpy('applySsoAccessToken') };
    appearanceApiSpy = {
      apiAppearanceSettingsMeGet: jasmine.createSpy('apiAppearanceSettingsMeGet').and.returnValue(
        of({
          palette: 'Teal',
          mode: 'System',
          effectivePalette: 'Teal',
          effectiveMode: 'System',
        }),
      ),
      apiAppearanceSettingsMePatch: jasmine.createSpy('apiAppearanceSettingsMePatch'),
    };
    brandingApiSpy = {
      apiBrandingSettingsEffectiveGet: jasmine
        .createSpy('apiBrandingSettingsEffectiveGet')
        .and.returnValue(of({ siteTitle: 'Ninlil', logoDataUri: null })),
    };

    await TestBed.configureTestingModule({
      imports: [SsoCallbackComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authServiceFake },
        { provide: AppearanceApiService, useValue: appearanceApiSpy },
        { provide: BrandingApiService, useValue: brandingApiSpy },
      ],
    }).compileComponents();

    navigateByUrlSpy = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);

    const fixture = TestBed.createComponent(SsoCallbackComponent);
    fixture.detectChanges();
    // ngOnInit starts completeSignIn() without awaiting it, and with zoneless change detection
    // fixture.whenStable() does not wait for it, so flush the task queue instead.
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
    return fixture;
  }

  it('applies the access token parsed out of the URL fragment and navigates to /dashboard', async () => {
    await createComponent('#access_token=at-value&expires_at=2026-01-01T00%3A00%3A00Z');

    expect(authServiceFake.applySsoAccessToken).toHaveBeenCalledWith('at-value');
    expect(navigateByUrlSpy).toHaveBeenCalledWith('/dashboard');
  });

  it('removes the token from the address bar once it has been read', async () => {
    await createComponent('#access_token=at-value');

    expect(window.location.hash).toBe('');
  });

  it('re-runs ThemeService.initialize()/BrandingService.initialize() before navigating away, the same as a local sign-in', async () => {
    await createComponent('#access_token=at-value');

    expect(appearanceApiSpy.apiAppearanceSettingsMeGet).toHaveBeenCalledTimes(1);
    expect(brandingApiSpy.apiBrandingSettingsEffectiveGet).toHaveBeenCalledTimes(1);
  });

  it('shows the failed state and never applies a token when the fragment is missing access_token', async () => {
    const fixture = await createComponent('#expires_at=2026-01-01T00%3A00%3A00Z');

    expect(fixture.componentInstance.failed()).toBe(true);
    expect(authServiceFake.applySsoAccessToken).not.toHaveBeenCalled();
    expect(navigateByUrlSpy).not.toHaveBeenCalled();
  });

  it('shows the failed state when this page loaded with no fragment at all', async () => {
    const fixture = await createComponent('');

    expect(fixture.componentInstance.failed()).toBe(true);
  });

  it('renders a "Back to sign in" link to /login in the failed state', async () => {
    const fixture = await createComponent('');
    const root = fixture.nativeElement as HTMLElement;

    const link = root.querySelector('a[routerLink="/login"]');
    expect(link).not.toBeNull();
  });
});
