import { TestBed } from '@angular/core/testing';
import { Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { permissionGuard } from './permission.guard';

describe('permissionGuard', () => {
  let authServiceFake: { isAuthenticated: jasmine.Spy; hasPermission: jasmine.Spy };
  let router: Router;

  beforeEach(() => {
    authServiceFake = {
      isAuthenticated: jasmine.createSpy('isAuthenticated'),
      hasPermission: jasmine.createSpy('hasPermission'),
    };

    TestBed.configureTestingModule({
      providers: [{ provide: AuthService, useValue: authServiceFake }],
    });

    router = TestBed.inject(Router);
  });

  it('redirects to /login when the caller is not signed in at all', () => {
    authServiceFake.isAuthenticated.and.returnValue(false);

    const guard = permissionGuard('zones:read');
    const result = TestBed.runInInjectionContext(() =>
      guard({} as never, { url: '/zones' } as RouterStateSnapshot),
    ) as UrlTree;

    expect(router.serializeUrl(result)).toBe('/login?returnUrl=%2Fzones');
    expect(authServiceFake.hasPermission).not.toHaveBeenCalled();
  });

  it('redirects to /forbidden when signed in but lacking the required permission', () => {
    authServiceFake.isAuthenticated.and.returnValue(true);
    authServiceFake.hasPermission.and.returnValue(false);

    const guard = permissionGuard('zones:read');
    const result = TestBed.runInInjectionContext(() =>
      guard({} as never, { url: '/zones' } as RouterStateSnapshot),
    ) as UrlTree;

    expect(router.serializeUrl(result)).toBe('/forbidden');
    expect(authServiceFake.hasPermission).toHaveBeenCalledWith('zones:read');
  });

  it('allows navigation when signed in and holding the required permission', () => {
    authServiceFake.isAuthenticated.and.returnValue(true);
    authServiceFake.hasPermission.and.returnValue(true);

    const guard = permissionGuard('zones:read');
    const result = TestBed.runInInjectionContext(() =>
      guard({} as never, { url: '/zones' } as RouterStateSnapshot),
    );

    expect(result).toBeTrue();
  });
});
