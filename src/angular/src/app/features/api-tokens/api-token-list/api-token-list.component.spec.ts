import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ApiTokensService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ApiTokenListComponent } from './api-token-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the token list: loading, each status and its color, and revoking with the
 * confirmation accepted, dismissed or failing. TestBed with a spy-backed `ApiTokensService` and
 * `NotificationService`.
 */
describe('ApiTokenListComponent', () => {
  let apiTokensApiSpy: { apiApiTokensGet: jasmine.Spy; apiApiTokensIdDelete: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    apiTokensApiSpy = jasmine.createSpyObj('ApiTokensService', [
      'apiApiTokensGet',
      'apiApiTokensIdDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiTokensApiSpy.apiApiTokensGet.and.returnValue(
      of([
        {
          id: 'token-1',
          name: 'CI pipeline',
          type: 'Static',
          createdAtUtc: '2026-01-01T00:00:00Z',
          expiresAtUtc: null,
          consumedAtUtc: null,
          revokedAtUtc: null,
        },
      ]),
    );

    await TestBed.configureTestingModule({
      imports: [ApiTokenListComponent],
      providers: [
        provideRouter([]),
        { provide: ApiTokensService, useValue: apiTokensApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ApiTokenListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads every token on init', async () => {
    const fixture = await createComponent();

    expect(apiTokensApiSpy.apiApiTokensGet).toHaveBeenCalledWith();
    expect(fixture.componentInstance.tokens().length).toBe(1);
  });

  it('status() reports Active for a fresh, unrevoked, unexpired static token', async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.status(fixture.componentInstance.tokens()[0])).toBe('Active');
  });

  it('status() reports Revoked once revokedAtUtc is set', async () => {
    const fixture = await createComponent();

    expect(
      fixture.componentInstance.status({
        ...fixture.componentInstance.tokens()[0],
        revokedAtUtc: '2026-02-01T00:00:00Z',
      }),
    ).toBe('Revoked');
  });

  it('status() reports Consumed for a one-time token already used', async () => {
    const fixture = await createComponent();

    expect(
      fixture.componentInstance.status({
        ...fixture.componentInstance.tokens()[0],
        type: 'OneTime',
        consumedAtUtc: '2026-02-01T00:00:00Z',
      }),
    ).toBe('Consumed');
  });

  it('status() reports Expired for a token past its own expiry', async () => {
    const fixture = await createComponent();

    expect(
      fixture.componentInstance.status({
        ...fixture.componentInstance.tokens()[0],
        expiresAtUtc: '2000-01-01T00:00:00Z',
      }),
    ).toBe('Expired');
  });

  it('statusColor() maps each status to a distinct badge color', async () => {
    const fixture = await createComponent();
    const token = fixture.componentInstance.tokens()[0];

    expect(fixture.componentInstance.statusColor(token)).toBe('success');
    expect(
      fixture.componentInstance.statusColor({ ...token, revokedAtUtc: '2026-02-01T00:00:00Z' }),
    ).toBe('danger');
    expect(
      fixture.componentInstance.statusColor({ ...token, expiresAtUtc: '2000-01-01T00:00:00Z' }),
    ).toBe('secondary');
  });

  it('revokeToken() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const revokePromise = fixture.componentInstance.revokeToken(
      fixture.componentInstance.tokens()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await revokePromise;

    expect(apiTokensApiSpy.apiApiTokensIdDelete).not.toHaveBeenCalled();
  });

  it('revokeToken() revokes and reloads once confirmed', async () => {
    const fixture = await createComponent();
    apiTokensApiSpy.apiApiTokensIdDelete.and.returnValue(of(undefined));

    const revokePromise = fixture.componentInstance.revokeToken(
      fixture.componentInstance.tokens()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await revokePromise;

    expect(apiTokensApiSpy.apiApiTokensIdDelete).toHaveBeenCalledWith({ id: 'token-1' });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('shows an error notification when revoke fails', async () => {
    const fixture = await createComponent();
    apiTokensApiSpy.apiApiTokensIdDelete.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 404 })),
    );

    const revokePromise = fixture.componentInstance.revokeToken(
      fixture.componentInstance.tokens()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await revokePromise;

    expect(notificationSpy.error).toHaveBeenCalled();
  });
});
