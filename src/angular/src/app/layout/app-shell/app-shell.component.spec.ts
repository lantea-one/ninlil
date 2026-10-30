import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { IconSetService } from '@coreui/icons-angular';
import { cilCog } from '@coreui/icons';
import { throwError } from 'rxjs';
import { AppShellComponent } from './app-shell.component';
import { AuthService } from '../../core/auth/auth.service';
import { BrandingService } from '../../core/branding/branding.service';
import { AppearanceSettingsService as AppearanceApiService } from '../../api-client';

/** A stand-in for `LoginComponent`, so the navigation in `logout()` resolves. */
@Component({ selector: 'app-test-login-stub', template: '' })
class LoginStubComponent {}

/**
 * These tests cover the app shell: which nav items each permission set shows (including the
 * any-of rule for Settings), logout, the CoreUI layout, the menu and collapse buttons' breakpoints,
 * the branding title and logo, and the theme switcher in the header. TestBed with a fake
 * `AuthService`, a fake `BrandingService` (two signals), a test router and a rejecting
 * appearance API.
 */
describe('AppShellComponent', () => {
  let authServiceFake: {
    hasPermission: jasmine.Spy;
    hasAnyPermission: (permissions: readonly string[]) => boolean;
    userName: ReturnType<typeof signal<string | null>>;
    logout: jasmine.Spy;
  };

  let siteTitle: ReturnType<typeof signal<string | null>>;
  let logoDataUri: ReturnType<typeof signal<string | null>>;

  beforeEach(async () => {
    siteTitle = signal<string | null>(null);
    logoDataUri = signal<string | null>(null);
    authServiceFake = {
      hasPermission: jasmine.createSpy('hasPermission'),
      // Answered through hasPermission, as AuthService does, so each test's hasPermission fake
      // decides both.
      hasAnyPermission: (permissions) =>
        permissions.some((permission) => authServiceFake.hasPermission(permission)),
      userName: signal<string | null>('someone'),
      logout: jasmine.createSpy('logout').and.resolveTo(undefined),
    };

    await TestBed.configureTestingModule({
      imports: [AppShellComponent],
      providers: [
        provideRouter([{ path: 'login', component: LoginStubComponent }]),
        { provide: AuthService, useValue: authServiceFake },
        { provide: BrandingService, useValue: { siteTitle, logoDataUri } },
        // The header's ThemeSwitcherComponent needs ThemeService, which needs this API service.
        // A rejecting fake is enough: these tests do not use the switcher.
        {
          provide: AppearanceApiService,
          useValue: {
            apiAppearanceSettingsMeGet: jasmine
              .createSpy('apiAppearanceSettingsMeGet')
              .and.returnValue(throwError(() => new Error('unauthorized'))),
            apiAppearanceSettingsMePatch: jasmine.createSpy('apiAppearanceSettingsMePatch'),
          },
        },
      ],
    }).compileComponents();

    // The app initializer in app.config.ts normally fills IconSetService; outside it the set is
    // empty, so seed the Services item's icon (`cilCog`).
    TestBed.inject(IconSetService).icons = { cilCog };
  });

  it('shows only the nav items the current permissions actually grant', () => {
    authServiceFake.hasPermission.and.callFake((permission: string) => permission === 'zones:read');

    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();

    const labels = fixture.componentInstance.visibleNavItems().map((item) => item.label);

    // Dashboard and API Tokens need no permission; Zones needs "zones:read", which this fake
    // grants. Every other item (TSIG Keys, Cloud Credentials, ...) must be absent.
    expect(labels).toContain('Dashboard');
    expect(labels).toContain('API Tokens');
    expect(labels).toContain('Zones');
    expect(labels).not.toContain('TSIG Keys');
    expect(labels).not.toContain('Users');
  });

  it('shows Presets & Settings to a user who can read any one settings page, not only presets', () => {
    authServiceFake.hasPermission.and.callFake(
      (permission: string) => permission === 'smtpsettings:read',
    );

    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();

    const labels = fixture.componentInstance.visibleNavItems().map((item) => item.label);
    expect(labels).toContain('Presets & Settings');
  });

  it('hides Presets & Settings from a user who can read no settings page', () => {
    authServiceFake.hasPermission.and.callFake((permission: string) => permission === 'zones:read');

    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();

    const labels = fixture.componentInstance.visibleNavItems().map((item) => item.label);
    expect(labels).not.toContain('Presets & Settings');
  });

  it('shows every nav item once every permission is granted', () => {
    authServiceFake.hasPermission.and.returnValue(true);

    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.visibleNavItems().length).toBe(24);
  });

  it('logs out through AuthService when the logout action is invoked', async () => {
    authServiceFake.hasPermission.and.returnValue(true);

    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();

    await fixture.componentInstance.logout();

    expect(authServiceFake.logout).toHaveBeenCalledTimes(1);
  });

  it('renders a real CoreUI sidebar/header shell, not the original Material toolbar/sidenav', () => {
    authServiceFake.hasPermission.and.returnValue(true);

    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('c-sidebar')).not.toBeNull();
    expect(root.querySelector('c-header')).not.toBeNull();
    expect(root.querySelector('mat-sidenav')).toBeNull();
    expect(root.querySelector('mat-toolbar')).toBeNull();
  });

  it('mounts the theme switcher in the header, its real permanent home now that this shell exists', () => {
    authServiceFake.hasPermission.and.returnValue(true);

    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();

    const header = (fixture.nativeElement as HTMLElement).querySelector('c-header');
    expect(header?.querySelector('app-theme-switcher')).not.toBeNull();
  });

  it('offers a visible menu button below lg and the collapse button only from lg', () => {
    authServiceFake.hasPermission.and.returnValue(true);

    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const menuButton = root.querySelector('c-header button[aria-label="Toggle sidebar"]');
    // CoreUI's header toggler draws nothing itself, so the button must hold an icon to be seen.
    expect(menuButton?.classList).toContain('d-lg-none');
    expect(menuButton?.querySelector('svg[name="cilMenu"]')).not.toBeNull();

    // The sidebar is hidden below lg, so its collapse button must not show between md and lg.
    const footer = root.querySelector('c-sidebar-footer');
    expect(footer?.classList).toContain('d-lg-flex');
    expect(footer?.classList).not.toContain('d-md-flex');
  });

  it('shows the default titles and the icon when no branding is set', () => {
    authServiceFake.hasPermission.and.returnValue(true);

    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.shell-brand-text')?.textContent?.trim()).toBe('Ninlil');
    expect(root.querySelector('.shell-title')?.textContent?.trim()).toBe('Ninlil Admin');
    expect(root.querySelector('img.shell-brand-logo')).toBeNull();
    expect(root.querySelector('.shell-brand-icon')).not.toBeNull();
  });

  it('shows the configured site title and logo in the sidebar and the title in the header', () => {
    authServiceFake.hasPermission.and.returnValue(true);
    siteTitle.set('Acme DNS');
    logoDataUri.set('data:image/png;base64,AAAA');

    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    expect(root.querySelector('.shell-brand-text')?.textContent?.trim()).toBe('Acme DNS');
    expect(root.querySelector('.shell-title')?.textContent?.trim()).toBe('Acme DNS');
    const logo = root.querySelector('img.shell-brand-logo');
    expect(logo?.getAttribute('src')).toBe('data:image/png;base64,AAAA');
    expect(logo?.getAttribute('alt')).toBe('Acme DNS');
    expect(root.querySelector('.shell-brand-icon')).toBeNull();
  });

  it('keeps an SVG logo as an image source instead of an unsafe link', () => {
    authServiceFake.hasPermission.and.returnValue(true);
    const svg = 'data:image/svg+xml;base64,PHN2Zy8+';
    logoDataUri.set(svg);

    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();

    const logo = (fixture.nativeElement as HTMLElement).querySelector('img.shell-brand-logo');
    expect(logo?.getAttribute('src')).toBe(svg);
  });
});
