import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { TwoFactorService } from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { NotificationService } from '../../../shared/notification/notification.service';
import { AccountSecurityComponent } from './account-security.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Security page: loading the status, authenticator setup and confirmation,
 * turning two-factor off, the e-mailed code, and recovery codes with the reveal-once dialog.
 * The changes that end the session (confirming, turning off, new recovery codes) are checked
 * through spy-backed `AuthService` and `Router`. TestBed with a spy-backed `TwoFactorService` and
 * `NotificationService`.
 */
describe('AccountSecurityComponent', () => {
  let twoFactorApiSpy: {
    apiAuthTwoFactorStatusGet: jasmine.Spy;
    apiAuthTwoFactorAuthenticatorSetupPost: jasmine.Spy;
    apiAuthTwoFactorAuthenticatorEnablePost: jasmine.Spy;
    apiAuthTwoFactorDisablePost: jasmine.Spy;
    apiAuthTwoFactorEmailSendPost: jasmine.Spy;
    apiAuthTwoFactorRecoveryCodesGeneratePost: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let authServiceSpy: { endSessionAfterSecurityChange: jasmine.Spy };
  let routerSpy: { navigateByUrl: jasmine.Spy };

  async function createComponent(configureApis?: () => void) {
    twoFactorApiSpy = jasmine.createSpyObj('TwoFactorService', [
      'apiAuthTwoFactorStatusGet',
      'apiAuthTwoFactorAuthenticatorSetupPost',
      'apiAuthTwoFactorAuthenticatorEnablePost',
      'apiAuthTwoFactorDisablePost',
      'apiAuthTwoFactorEmailSendPost',
      'apiAuthTwoFactorRecoveryCodesGeneratePost',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
    authServiceSpy = jasmine.createSpyObj('AuthService', ['endSessionAfterSecurityChange']);
    authServiceSpy.endSessionAfterSecurityChange.and.resolveTo();
    routerSpy = jasmine.createSpyObj('Router', ['navigateByUrl']);
    routerSpy.navigateByUrl.and.resolveTo(true);

    twoFactorApiSpy.apiAuthTwoFactorStatusGet.and.returnValue(
      of({
        isEnabled: false,
        hasAuthenticator: false,
        canReceiveEmailCode: true,
        recoveryCodesRemaining: 0,
      }),
    );

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [AccountSecurityComponent],
      providers: [
        { provide: TwoFactorService, useValue: twoFactorApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: Router, useValue: routerSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(AccountSecurityComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current status on init', async () => {
    const fixture = await createComponent();

    expect(twoFactorApiSpy.apiAuthTwoFactorStatusGet).toHaveBeenCalled();
    expect(fixture.componentInstance.status()).toEqual({
      isEnabled: false,
      hasAuthenticator: false,
      canReceiveEmailCode: true,
      recoveryCodesRemaining: 0,
    });
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('startAuthenticatorSetup() stores the returned shared key/URI as the pending setup', async () => {
    const fixture = await createComponent();
    twoFactorApiSpy.apiAuthTwoFactorAuthenticatorSetupPost.and.returnValue(
      of({ sharedKey: 'ABC123', authenticatorUri: 'otpauth://totp/x?secret=ABC123' }),
    );

    await fixture.componentInstance.startAuthenticatorSetup();

    expect(fixture.componentInstance.pendingSetup()).toEqual({
      sharedKey: 'ABC123',
      authenticatorUri: 'otpauth://totp/x?secret=ABC123',
    });
  });

  it('confirmAuthenticatorSetup() enables two-factor, clears the pending setup, and signs out with a notice', async () => {
    const fixture = await createComponent();
    twoFactorApiSpy.apiAuthTwoFactorAuthenticatorEnablePost.and.returnValue(of(undefined));
    fixture.componentInstance.pendingSetup.set({
      sharedKey: 'ABC123',
      authenticatorUri: 'otpauth://totp/x',
    });
    fixture.componentInstance.enableForm.controls.code.setValue('123456');

    await fixture.componentInstance.confirmAuthenticatorSetup();

    expect(twoFactorApiSpy.apiAuthTwoFactorAuthenticatorEnablePost).toHaveBeenCalledWith({
      twoFactorCodeRequest: { code: '123456' },
    });
    expect(fixture.componentInstance.pendingSetup()).toBeNull();
    // The server ended every session of the account, this one included.
    expect(authServiceSpy.endSessionAfterSecurityChange).toHaveBeenCalledWith(
      jasmine.stringContaining('Two-factor authentication is on'),
    );
    expect(routerSpy.navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('confirmAuthenticatorSetup() shows an error message when the code is rejected', async () => {
    const fixture = await createComponent();
    twoFactorApiSpy.apiAuthTwoFactorAuthenticatorEnablePost.and.returnValue(
      throwError(
        () => new HttpErrorResponse({ status: 400, error: 'The provided code is not valid.' }),
      ),
    );
    fixture.componentInstance.enableForm.controls.code.setValue('000000');

    await fixture.componentInstance.confirmAuthenticatorSetup();

    expect(fixture.componentInstance.errorMessage()).toBe('The provided code is not valid.');
    expect(authServiceSpy.endSessionAfterSecurityChange).not.toHaveBeenCalled();
  });

  it('disable() turns off two-factor and signs out with a notice', async () => {
    const fixture = await createComponent();
    twoFactorApiSpy.apiAuthTwoFactorDisablePost.and.returnValue(of(undefined));
    fixture.componentInstance.disableForm.controls.code.setValue('123456');

    await fixture.componentInstance.disable();

    expect(twoFactorApiSpy.apiAuthTwoFactorDisablePost).toHaveBeenCalledWith({
      twoFactorCodeRequest: { code: '123456' },
    });
    expect(authServiceSpy.endSessionAfterSecurityChange).toHaveBeenCalledWith(
      jasmine.stringContaining('Two-factor authentication is off'),
    );
    expect(routerSpy.navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('disable() does nothing while its own form is invalid', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.disable();

    expect(twoFactorApiSpy.apiAuthTwoFactorDisablePost).not.toHaveBeenCalled();
  });

  it('sendEmailCode() calls the endpoint and shows a success notification', async () => {
    const fixture = await createComponent();
    twoFactorApiSpy.apiAuthTwoFactorEmailSendPost.and.returnValue(of(undefined));

    await fixture.componentInstance.sendEmailCode();

    expect(twoFactorApiSpy.apiAuthTwoFactorEmailSendPost).toHaveBeenCalled();
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('sendEmailCode() shows an error notification on failure', async () => {
    const fixture = await createComponent();
    twoFactorApiSpy.apiAuthTwoFactorEmailSendPost.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 400 })),
    );

    await fixture.componentInstance.sendEmailCode();

    expect(notificationSpy.error).toHaveBeenCalled();
  });

  it('generateRecoveryCodes() shows the reveal-once dialog with all 10 codes, without signing out yet', async () => {
    const fixture = await createComponent();
    const codes = Array.from({ length: 10 }, (_, index) => `CODE-${index}`);
    twoFactorApiSpy.apiAuthTwoFactorRecoveryCodesGeneratePost.and.returnValue(of({ codes }));

    await fixture.componentInstance.generateRecoveryCodes();

    expect(twoFactorApiSpy.apiAuthTwoFactorRecoveryCodesGeneratePost).toHaveBeenCalled();
    expect(fixture.componentInstance.revealDialogVisible()).toBeTrue();
    expect(fixture.componentInstance.revealDialogData()).toEqual(
      jasmine.objectContaining({
        secrets: codes.map((value, index) => ({ label: `Code ${index + 1}`, value })),
      }),
    );
    // The sign-out waits until the user has copied the codes and closed the dialog.
    expect(authServiceSpy.endSessionAfterSecurityChange).not.toHaveBeenCalled();
  });

  it('onSecretDialogClosed() hides the dialog and signs out with a notice, once the recovery-codes dialog is dismissed', async () => {
    const fixture = await createComponent();
    const codes = Array.from({ length: 10 }, (_, index) => `CODE-${index}`);
    twoFactorApiSpy.apiAuthTwoFactorRecoveryCodesGeneratePost.and.returnValue(of({ codes }));
    await fixture.componentInstance.generateRecoveryCodes();

    await fixture.componentInstance.onSecretDialogClosed();

    expect(fixture.componentInstance.revealDialogVisible()).toBeFalse();
    expect(authServiceSpy.endSessionAfterSecurityChange).toHaveBeenCalledWith(
      jasmine.stringContaining('New recovery codes were generated'),
    );
    expect(routerSpy.navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('generateRecoveryCodes() shows an error notification on failure and never shows the dialog', async () => {
    const fixture = await createComponent();
    twoFactorApiSpy.apiAuthTwoFactorRecoveryCodesGeneratePost.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: 'Two-factor authentication must already be enabled.',
          }),
      ),
    );

    await fixture.componentInstance.generateRecoveryCodes();

    expect(notificationSpy.error).toHaveBeenCalled();
    expect(fixture.componentInstance.revealDialogVisible()).toBeFalse();
  });
});
