import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { UsersService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import {
  ResetPasswordDialogComponent,
  ResetPasswordDialogTarget,
} from './reset-password-dialog.component';

/**
 * These tests cover the password reset dialog: generating and revealing a password, sending a
 * link, errors, guards and closing. TestBed with spy-backed `UsersService` and
 * `NotificationService`.
 */
describe('ResetPasswordDialogComponent', () => {
  let fixture: ComponentFixture<ResetPasswordDialogComponent>;
  let usersApiSpy: { apiUsersIdResetPasswordPost: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  const target: ResetPasswordDialogTarget = {
    id: 'user-1',
    userName: 'jsmith',
    email: 'jsmith@example.com',
  };

  async function createWith(visible = true): Promise<void> {
    usersApiSpy = jasmine.createSpyObj('UsersService', ['apiUsersIdResetPasswordPost']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    await TestBed.configureTestingModule({
      imports: [ResetPasswordDialogComponent],
      providers: [
        { provide: UsersService, useValue: usersApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ResetPasswordDialogComponent);
    fixture.componentRef.setInput('visible', visible);
    fixture.componentRef.setInput('target', target);
    fixture.detectChanges();
  }

  it('submit() with the AutoGenerate mode reveals the generated password and closes the mode-selection step', async () => {
    await createWith();
    usersApiSpy.apiUsersIdResetPasswordPost.and.returnValue(
      of({ mode: 'AutoGenerate', generatedPassword: 'xK9#mQ2wLp7$vN4rT8bE' }),
    );
    const visibleChangeSpy = jasmine.createSpy('visibleChange');
    fixture.componentInstance.visibleChange.subscribe(visibleChangeSpy);

    await fixture.componentInstance.submit();

    expect(usersApiSpy.apiUsersIdResetPasswordPost).toHaveBeenCalledWith({
      id: 'user-1',
      adminResetPasswordRequest: { mode: 'AutoGenerate' },
    });
    expect(visibleChangeSpy).toHaveBeenCalledWith(false);
    expect(fixture.componentInstance.revealDialogVisible()).toBeTrue();
    expect(fixture.componentInstance.revealDialogData()?.secrets).toEqual([
      { label: 'New password', value: 'xK9#mQ2wLp7$vN4rT8bE' },
    ]);
  });

  it('submit() with the EmailLink mode shows a success notification and emits completed', async () => {
    await createWith();
    fixture.componentInstance.modeForm.controls.mode.setValue('EmailLink');
    usersApiSpy.apiUsersIdResetPasswordPost.and.returnValue(
      of({ mode: 'EmailLink', generatedPassword: null }),
    );
    const completedSpy = jasmine.createSpy('completed');
    fixture.componentInstance.completed.subscribe(completedSpy);

    await fixture.componentInstance.submit();

    expect(usersApiSpy.apiUsersIdResetPasswordPost).toHaveBeenCalledWith({
      id: 'user-1',
      adminResetPasswordRequest: { mode: 'EmailLink' },
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      'Password reset link sent to jsmith@example.com.',
    );
    expect(completedSpy).toHaveBeenCalled();
    expect(fixture.componentInstance.revealDialogVisible()).toBeFalse();
  });

  it('submit() shows a clear error message on failure -- e.g. SMTP not configured -- and stays open', async () => {
    await createWith();
    fixture.componentInstance.modeForm.controls.mode.setValue('EmailLink');
    usersApiSpy.apiUsersIdResetPasswordPost.and.returnValue(
      throwError(
        () => new HttpErrorResponse({ status: 400, error: { detail: 'SMTP is not configured.' } }),
      ),
    );

    await fixture.componentInstance.submit();

    expect(fixture.componentInstance.errorMessage()).toBe('SMTP is not configured.');
    expect(fixture.componentInstance.submitting()).toBeFalse();
  });

  it('submit() does nothing while already submitting or with no target', async () => {
    await createWith();
    fixture.componentInstance.submitting.set(true);

    await fixture.componentInstance.submit();

    expect(usersApiSpy.apiUsersIdResetPasswordPost).not.toHaveBeenCalled();
  });

  it('onModalVisibleChange(false) clears the error and emits visibleChange(false)', async () => {
    await createWith();
    fixture.componentInstance.errorMessage.set('some error');
    const visibleChangeSpy = jasmine.createSpy('visibleChange');
    fixture.componentInstance.visibleChange.subscribe(visibleChangeSpy);

    fixture.componentInstance.onModalVisibleChange(false);

    expect(fixture.componentInstance.errorMessage()).toBeNull();
    expect(visibleChangeSpy).toHaveBeenCalledWith(false);
  });

  it('onRevealDialogClosed() hides the reveal dialog and emits completed', async () => {
    await createWith();
    fixture.componentInstance.revealDialogVisible.set(true);
    const completedSpy = jasmine.createSpy('completed');
    fixture.componentInstance.completed.subscribe(completedSpy);

    fixture.componentInstance.onRevealDialogClosed();

    expect(fixture.componentInstance.revealDialogVisible()).toBeFalse();
    expect(completedSpy).toHaveBeenCalled();
  });
});
