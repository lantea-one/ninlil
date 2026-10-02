import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ApiTokensService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ApiTokenFormComponent } from './api-token-form.component';

/**
 * These tests cover the New API Token form: validation, the request (including the expiry
 * conversion), the reveal-once dialog before navigating, and errors. TestBed with a spy-backed
 * `ApiTokensService` and a spied `Router.navigate`.
 */
describe('ApiTokenFormComponent', () => {
  let apiTokensApiSpy: { apiApiTokensPost: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  async function createComponent() {
    apiTokensApiSpy = jasmine.createSpyObj('ApiTokensService', ['apiApiTokensPost']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    await TestBed.configureTestingModule({
      imports: [ApiTokenFormComponent],
      providers: [
        provideRouter([]),
        { provide: ApiTokensService, useValue: apiTokensApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(ApiTokenFormComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent();
    fixture.componentInstance.form.patchValue({ name: '' });

    await fixture.componentInstance.save();

    expect(apiTokensApiSpy.apiApiTokensPost).not.toHaveBeenCalled();
  });

  it('save() POSTs the request with a null expiry when left blank and shows the reveal-once dialog without navigating yet', async () => {
    const fixture = await createComponent();
    apiTokensApiSpy.apiApiTokensPost.and.returnValue(
      of({ rawToken: 'raw-token-value', token: { id: 'token-1', name: 'CI pipeline' } }),
    );
    fixture.componentInstance.form.patchValue({ name: 'CI pipeline' });

    await fixture.componentInstance.save();

    expect(apiTokensApiSpy.apiApiTokensPost).toHaveBeenCalledWith({
      createApiTokenRequest: { name: 'CI pipeline', oneTime: false, expiresAtUtc: null },
    });
    expect(fixture.componentInstance.revealDialogVisible()).toBeTrue();
    expect(fixture.componentInstance.revealDialogData()).toEqual(
      jasmine.objectContaining({
        secrets: [{ label: 'Token', value: 'raw-token-value' }],
      }),
    );
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('onSecretDialogClosed() hides the dialog and navigates to the list, once dismissed', async () => {
    const fixture = await createComponent();
    apiTokensApiSpy.apiApiTokensPost.and.returnValue(
      of({ rawToken: 'raw-token-value', token: { id: 'token-1' } }),
    );
    fixture.componentInstance.form.patchValue({ name: 'CI pipeline' });
    await fixture.componentInstance.save();

    fixture.componentInstance.onSecretDialogClosed();

    expect(fixture.componentInstance.revealDialogVisible()).toBeFalse();
    expect(navigateSpy).toHaveBeenCalledWith(['/api-tokens']);
  });

  it('save() converts a supplied local expiry into an ISO string', async () => {
    const fixture = await createComponent();
    apiTokensApiSpy.apiApiTokensPost.and.returnValue(
      of({ rawToken: 'raw-token-value', token: { id: 'token-1' } }),
    );
    fixture.componentInstance.form.patchValue({
      name: 'CI pipeline',
      oneTime: true,
      expiresAtUtc: '2030-01-01T00:00',
    });

    await fixture.componentInstance.save();

    const request = apiTokensApiSpy.apiApiTokensPost.calls.mostRecent().args[0] as {
      createApiTokenRequest: { oneTime: boolean; expiresAtUtc: string | null };
    };
    expect(request.createApiTokenRequest.oneTime).toBeTrue();
    expect(request.createApiTokenRequest.expiresAtUtc).toBe(
      new Date('2030-01-01T00:00').toISOString(),
    );
  });

  it('shows an error message and does not navigate when creation fails', async () => {
    const fixture = await createComponent();
    apiTokensApiSpy.apiApiTokensPost.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 500 })),
    );
    fixture.componentInstance.form.patchValue({ name: 'CI pipeline' });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toContain('Could not create this token.');
    expect(navigateSpy).not.toHaveBeenCalled();
  });
});
