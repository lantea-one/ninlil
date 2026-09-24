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

/** A trivial stand-in for the real `LoginComponent`, so `logout()`'s own navigation resolves. */
@Component({ selector: 'app-test-login-stub', template: '' })
class LoginStubComponent {}

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
      // Answered through hasPermission, as the real AuthService does, so each test's own
      // hasPermission fake decides both.
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
        // The shell now mounts `ThemeSwitcherComponent` (its own real, permanent home) in the
        // header -- it depends on `ThemeService`, which depends on this API service. A
        // rejecting fake is enough here: nothing in this spec exercises the switcher itself.
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

    // The "Services" nav item (`nav-item.model.ts`) renders a real `cIcon` (`cilCog`). Outside a
    // full app bootstrap, `IconSetService.icons` starts empty -- `app.config.ts`'s own
    // `provideAppInitializer` is what normally populates it -- so this fixture seeds the one icon
    // it actually renders directly, rather than leaving `cIcon` to warn on every render.
    TestBed.inject(IconSetService).icons = { cilCog };
  });

  it('shows only the nav items the current permissions actually grant', () => {
    authServiceFake.hasPermission.and.callFake((permission: string) => permission === 'zones:read');

    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();

    const labels = fixture.componentInstance.visibleNavItems().map((item) => item.label);

    // Dashboard and API Tokens require no specific permission, so they are always visible;
    // Zones requires "zones:read", which this fake grants; every other permission-gated item
    // (TSIG Keys, Cloud Credentials, ...) must be absent.
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
