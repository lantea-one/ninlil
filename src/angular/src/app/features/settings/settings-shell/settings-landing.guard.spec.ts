import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { settingsLandingGuard } from './settings-landing.guard';

describe('settingsLandingGuard', () => {
  let grantedPermissions: string[];
  let router: Router;

  beforeEach(() => {
    grantedPermissions = [];
    TestBed.configureTestingModule({
      providers: [
        {
          provide: AuthService,
          useValue: {
            hasPermission: (permission: string) => grantedPermissions.includes(permission),
          },
        },
      ],
    });
    router = TestBed.inject(Router);
  });

  function runGuard() {
    return TestBed.runInInjectionContext(() =>
      settingsLandingGuard(
        {} as ActivatedRouteSnapshot,
        { url: '/settings' } as RouterStateSnapshot,
      ),
    );
  }

  it('shows the presets page to a user holding presets:read', () => {
    grantedPermissions = ['presets:read', 'smtpsettings:read'];

    expect(runGuard()).toBeTrue();
  });

  it('sends a user without presets:read to the first settings page they can read', () => {
    grantedPermissions = ['smtpsettings:read', 'dns64settings:read'];

    // DNS64 comes before SMTP in the settings navigation, so it is the first readable page.
    expect(router.serializeUrl(runGuard() as UrlTree)).toBe('/settings/dns64');
  });

  it('sends a user who can read no settings page to /forbidden', () => {
    grantedPermissions = ['zones:read'];

    expect(router.serializeUrl(runGuard() as UrlTree)).toBe('/forbidden');
  });
});
