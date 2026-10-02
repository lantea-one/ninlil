import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

/**
 * These tests cover `authInterceptor`: which requests get a Bearer header, the refresh-and-retry
 * after a 401, the redirect to `/login` when refresh fails, and the cases that must never refresh
 * (anonymous auth endpoints and requests sent without a token). A real `HttpClient` runs against
 * `HttpTestingController`, with a fake `AuthService` and a spied `Router.navigateByUrl`.
 */
describe('authInterceptor', () => {
  let httpClient: HttpClient;
  let httpMock: HttpTestingController;
  let authServiceFake: { accessToken: string | null; refresh: jasmine.Spy };
  let navigateByUrlSpy: jasmine.Spy;

  beforeEach(() => {
    authServiceFake = { accessToken: null, refresh: jasmine.createSpy('refresh') };

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: authServiceFake },
      ],
    });

    httpClient = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    navigateByUrlSpy = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
  });

  afterEach(() => httpMock.verify());

  it('attaches a bearer Authorization header when an access token is present', () => {
    authServiceFake.accessToken = 'the-access-token';

    httpClient.get('/api/zones').subscribe();

    const request = httpMock.expectOne('/api/zones');
    expect(request.request.headers.get('Authorization')).toBe('Bearer the-access-token');
    request.flush({});
  });

  it('sends no Authorization header at all when there is no access token', () => {
    httpClient.get('/api/zones').subscribe();

    const request = httpMock.expectOne('/api/zones');
    expect(request.request.headers.has('Authorization')).toBeFalse();
    request.flush({});
  });

  it('never attaches an Authorization header to an /api/auth/ request, even with a token present', () => {
    authServiceFake.accessToken = 'the-access-token';

    httpClient.post('/api/auth/login', {}).subscribe();

    const request = httpMock.expectOne('/api/auth/login');
    expect(request.request.headers.has('Authorization')).toBeFalse();
    request.flush({});
  });

  it('attaches an Authorization header to a self-service /api/auth/two-factor/... request, unlike a true anonymous /api/auth/ endpoint', () => {
    authServiceFake.accessToken = 'the-access-token';

    httpClient.get('/api/auth/two-factor/status').subscribe();

    const request = httpMock.expectOne('/api/auth/two-factor/status');
    expect(request.request.headers.get('Authorization')).toBe('Bearer the-access-token');
    request.flush({});
  });

  it('attempts a silent refresh for a 401 from /api/auth/two-factor/..., unlike a true anonymous /api/auth/ endpoint', (done) => {
    authServiceFake.accessToken = 'expired-access-token';
    authServiceFake.refresh.and.callFake(async () => {
      authServiceFake.accessToken = 'fresh-access-token';
      return true;
    });

    httpClient.get('/api/auth/two-factor/status').subscribe({
      next: () => {
        expect(authServiceFake.refresh).toHaveBeenCalledTimes(1);
        done();
      },
    });

    const firstAttempt = httpMock.expectOne('/api/auth/two-factor/status');
    firstAttempt.flush('unauthorized', { status: 401, statusText: 'Unauthorized' });

    Promise.resolve()
      .then(() => Promise.resolve())
      .then(() => {
        const retriedAttempt = httpMock.expectOne('/api/auth/two-factor/status');
        expect(retriedAttempt.request.headers.get('Authorization')).toBe(
          'Bearer fresh-access-token',
        );
        retriedAttempt.flush({});
      });
  });

  it('silently refreshes and retries once after a 401 on a non-auth request', (done) => {
    authServiceFake.accessToken = 'expired-access-token';
    authServiceFake.refresh.and.callFake(async () => {
      authServiceFake.accessToken = 'fresh-access-token';
      return true;
    });

    httpClient.get('/api/zones').subscribe({
      next: (body) => {
        expect(body).toEqual({ ok: true });
        expect(authServiceFake.refresh).toHaveBeenCalledTimes(1);
        expect(navigateByUrlSpy).not.toHaveBeenCalled();
        done();
      },
    });

    const firstAttempt = httpMock.expectOne('/api/zones');
    expect(firstAttempt.request.headers.get('Authorization')).toBe('Bearer expired-access-token');
    firstAttempt.flush('unauthorized', { status: 401, statusText: 'Unauthorized' });

    // The retry is sent once the fake refresh's promise resolves. This test is not fakeAsync,
    // so the second expectOne waits a couple of microtasks with Promise.resolve().then().
    Promise.resolve()
      .then(() => Promise.resolve())
      .then(() => {
        const retriedAttempt = httpMock.expectOne('/api/zones');
        expect(retriedAttempt.request.headers.get('Authorization')).toBe(
          'Bearer fresh-access-token',
        );
        retriedAttempt.flush({ ok: true });
      });
  });

  it('routes to /login and gives up when the silent refresh itself fails', (done) => {
    authServiceFake.accessToken = 'expired-access-token';
    authServiceFake.refresh.and.resolveTo(false);

    httpClient.get('/api/zones').subscribe({
      error: () => {
        expect(navigateByUrlSpy).toHaveBeenCalledWith('/login');
        done();
      },
    });

    const request = httpMock.expectOne('/api/zones');
    request.flush('unauthorized', { status: 401, statusText: 'Unauthorized' });
  });

  it('never attempts a refresh or routes to /login for a 401 on a request that was already anonymous (no access token attached)', (done) => {
    // No access token: this is a startup call such as BrandingService.initialize() made before
    // sign-in. Redirecting here would pull a visitor off /forgot-password or /reset-password.
    httpClient.get('/api/branding-settings/effective').subscribe({
      error: () => {
        expect(authServiceFake.refresh).not.toHaveBeenCalled();
        expect(navigateByUrlSpy).not.toHaveBeenCalled();
        done();
      },
    });

    const request = httpMock.expectOne('/api/branding-settings/effective');
    request.flush('unauthorized', { status: 401, statusText: 'Unauthorized' });
  });

  it('never attempts a refresh for a 401 returned by an /api/auth/ endpoint itself', (done) => {
    httpClient.post('/api/auth/login', {}).subscribe({
      error: () => {
        expect(authServiceFake.refresh).not.toHaveBeenCalled();
        done();
      },
    });

    const request = httpMock.expectOne('/api/auth/login');
    request.flush('bad credentials', { status: 401, statusText: 'Unauthorized' });
  });
});
