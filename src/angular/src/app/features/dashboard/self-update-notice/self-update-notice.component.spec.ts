import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { SelfUpdateService } from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { SelfUpdateNoticeComponent } from './self-update-notice.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the update banner: hidden without an update, shown with one, and no request
 * without `selfupdate:read`. TestBed with spy-backed `SelfUpdateService` and `AuthService`.
 */
describe('SelfUpdateNoticeComponent', () => {
  let apiSpy: { apiSelfUpdateGet: jasmine.Spy };
  let authSpy: { hasPermission: jasmine.Spy };

  async function createComponent(
    hasPermission: boolean,
    statusResponse?: unknown,
    canOpenSettings = true,
  ) {
    apiSpy = jasmine.createSpyObj('SelfUpdateService', ['apiSelfUpdateGet']);
    authSpy = jasmine.createSpyObj('AuthService', ['hasPermission']);
    authSpy.hasPermission.and.callFake(
      (permission: string) =>
        hasPermission && (permission !== 'selfupdatesettings:read' || canOpenSettings),
    );
    apiSpy.apiSelfUpdateGet.and.returnValue(
      of(
        statusResponse ?? {
          configured: false,
          currentVersion: '1.0.0.0',
          latestKnownVersion: null,
          updateAvailable: false,
        },
      ),
    );

    await TestBed.configureTestingModule({
      imports: [SelfUpdateNoticeComponent],
      providers: [
        provideRouter([]),
        { provide: SelfUpdateService, useValue: apiSpy },
        { provide: AuthService, useValue: authSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SelfUpdateNoticeComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('stays hidden when no update is available', async () => {
    const fixture = await createComponent(true);

    expect(fixture.componentInstance.updateAvailable()).toBeFalse();
  });

  it('shows the notice when a newer version is available', async () => {
    const fixture = await createComponent(true, {
      configured: true,
      currentVersion: '1.0.0.0',
      latestKnownVersion: '2.0.0.0',
      updateAvailable: true,
    });

    expect(fixture.componentInstance.updateAvailable()).toBeTrue();
    expect(fixture.componentInstance.latestKnownVersion()).toBe('2.0.0.0');
    expect(fixture.nativeElement.textContent).toContain('2.0.0.0');
  });

  it('never fetches when the caller lacks the selfupdate:read permission', async () => {
    await createComponent(false);

    expect(apiSpy.apiSelfUpdateGet).not.toHaveBeenCalled();
  });

  const availableStatus = {
    configured: true,
    currentVersion: '1.0.0.0',
    latestKnownVersion: '2.0.0.0',
    updateAvailable: true,
  };

  it('links the settings page when the user holds selfupdatesettings:read', async () => {
    const fixture = await createComponent(true, availableStatus, true);

    const link = (fixture.nativeElement as HTMLElement).querySelector(
      'a[href="/settings/self-update"]',
    );
    expect(link).not.toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Ask an administrator');
  });

  it('shows no link and asks for an administrator without selfupdatesettings:read', async () => {
    const fixture = await createComponent(true, availableStatus, false);

    expect(
      (fixture.nativeElement as HTMLElement).querySelector('a[href="/settings/self-update"]'),
    ).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Ask an administrator to update.');
  });
});
