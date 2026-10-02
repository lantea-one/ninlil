import { TestBed } from '@angular/core/testing';
import { Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { authGuard } from './auth.guard';

/**
 * These tests cover `authGuard`: a signed-in session passes and a visitor without one is sent to
 * `/login` with a `returnUrl`. The guard runs in a TestBed injection context with a fake
 * `AuthService`.
 */
describe('authGuard', () => {
  let authServiceFake: { isAuthenticated: jasmine.Spy };
  let router: Router;

  beforeEach(() => {
    authServiceFake = { isAuthenticated: jasmine.createSpy('isAuthenticated') };

    TestBed.configureTestingModule({
      providers: [{ provide: AuthService, useValue: authServiceFake }],
    });

    router = TestBed.inject(Router);
  });

  it('allows navigation once a session is signed in', () => {
    authServiceFake.isAuthenticated.and.returnValue(true);

    const result = TestBed.runInInjectionContext(() =>
      authGuard({} as never, { url: '/zones' } as RouterStateSnapshot),
    );

    expect(result).toBeTrue();
  });

  it('redirects to /login with the attempted URL preserved when signed out', () => {
    authServiceFake.isAuthenticated.and.returnValue(false);

    const result = TestBed.runInInjectionContext(() =>
      authGuard({} as never, { url: '/zones' } as RouterStateSnapshot),
    ) as UrlTree;

    expect(router.serializeUrl(result)).toBe('/login?returnUrl=%2Fzones');
  });
});
