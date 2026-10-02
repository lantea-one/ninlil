import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { IconSetService } from '@coreui/icons-angular';
import { cilCog } from '@coreui/icons';
import { throwError } from 'rxjs';
import { AppShellComponent } from './app-shell.component';
import { AuthService } from '../../core/auth/auth.service';
import { AppearanceSettingsService as AppearanceApiService } from '../../api-client';

/** A stand-in for `LoginComponent`, so the navigation in `logout()` resolves. */
@Component({ selector: 'app-test-login-stub', template: '' })
class LoginStubComponent {}

/**
 * These tests cover the app shell: which nav items each permission set shows (including the
 * any-of rule for Settings), logout, the CoreUI layout and the theme switcher in the header.
 * TestBed with a fake `AuthService`, a test router and a rejecting appearance API.
 */
describe('AppShellComponent', () => {
  let authServiceFake: {
    hasPermission: jasmine.Spy;
    hasAnyPermission: (permissions: readonly string[]) => boolean;
    userName: ReturnType<typeof signal<string | null>>;
    logout: jasmine.Spy;
  };

  beforeEach(async () => {
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
});
