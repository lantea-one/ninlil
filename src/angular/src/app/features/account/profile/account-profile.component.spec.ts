import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { UsersService } from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { NotificationService } from '../../../shared/notification/notification.service';
import { buildGravatarUrl } from '../../../shared/gravatar/gravatar.util';
import { AccountProfileComponent } from './account-profile.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Profile page: loading, the Gravatar URL and initials fallback, saving
 * (trimming, `null` for cleared fields), and changing the password with its validation and errors, and the sign-out that follows a successful
 * change. TestBed with spy-backed `UsersService`, `NotificationService`, `AuthService` and `Router`.
 */
describe('AccountProfileComponent', () => {
  let usersApiSpy: {
    apiUsersMeGet: jasmine.Spy;
    apiUsersMePatch: jasmine.Spy;
    apiUsersMeChangePasswordPost: jasmine.Spy;
    apiUsersMeEmailPost: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let authServiceSpy: { endSessionAfterSecurityChange: jasmine.Spy };
  let routerSpy: { navigateByUrl: jasmine.Spy };

  const initialProfile = {
    id: '11111111-1111-1111-1111-111111111111',
    userName: 'jsmith',
    email: 'jsmith@example.com',
    displayName: 'Jamie Smith',
    phoneNumber: null,
  };

  async function createComponent(configureApis?: () => void) {
    usersApiSpy = jasmine.createSpyObj('UsersService', [
      'apiUsersMeGet',
      'apiUsersMePatch',
      'apiUsersMeChangePasswordPost',
      'apiUsersMeEmailPost',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
    authServiceSpy = jasmine.createSpyObj('AuthService', ['endSessionAfterSecurityChange']);
    authServiceSpy.endSessionAfterSecurityChange.and.resolveTo();
    routerSpy = jasmine.createSpyObj('Router', ['navigateByUrl']);
    routerSpy.navigateByUrl.and.resolveTo(true);

    usersApiSpy.apiUsersMeGet.and.returnValue(of(initialProfile));

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [AccountProfileComponent],
      providers: [
        { provide: UsersService, useValue: usersApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: Router, useValue: routerSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(AccountProfileComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current profile on init and populates the form', async () => {
    const fixture = await createComponent();

    expect(usersApiSpy.apiUsersMeGet).toHaveBeenCalled();
    expect(fixture.componentInstance.profile()).toEqual(initialProfile);
    expect(fixture.componentInstance.loading()).toBeFalse();
    expect(fixture.componentInstance.profileForm.getRawValue()).toEqual({
      displayName: 'Jamie Smith',
      phoneNumber: '',
    });
  });

  it('shows an error message when loading the profile fails', async () => {
    const fixture = await createComponent(() => {
      usersApiSpy.apiUsersMeGet.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 500 })),
      );
    });

    // A 500 with no body: extractErrorMessage returns the fallback text plus the status.
    expect(fixture.componentInstance.errorMessage()).toBe(
      'Could not load your profile. (HTTP 500)',
    );
  });

  it('computes the Gravatar avatar URL from the loaded profile e-mail address', async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.avatarUrl()).toBe(buildGravatarUrl('jsmith@example.com', 192));
  });

  it('onAvatarError() switches to the local initials fallback', async () => {
    const fixture = await createComponent();
    expect(fixture.componentInstance.avatarFailed()).toBeFalse();

    fixture.componentInstance.onAvatarError();

    expect(fixture.componentInstance.avatarFailed()).toBeTrue();
    // "Jamie Smith" gives "JS", read from the loaded profile.
    expect(fixture.componentInstance.avatarInitials()).toBe('JS');
  });

  it('falls back to the sign-in user name initial when no display name is set', async () => {
    const fixture = await createComponent(() => {
      usersApiSpy.apiUsersMeGet.and.returnValue(of({ ...initialProfile, displayName: null }));
    });

    expect(fixture.componentInstance.avatarInitials()).toBe('J');
  });

  it('save() sends trimmed display name/phone number and applies the response', async () => {
    const fixture = await createComponent();
    const updated = { ...initialProfile, displayName: 'Updated Name', phoneNumber: '+1-555-0100' };
    usersApiSpy.apiUsersMePatch.and.returnValue(of(updated));
    fixture.componentInstance.profileForm.controls.displayName.setValue('  Updated Name  ');
    fixture.componentInstance.profileForm.controls.phoneNumber.setValue('+1-555-0100');

    await fixture.componentInstance.save();

    expect(usersApiSpy.apiUsersMePatch).toHaveBeenCalledWith({
      body: { displayName: 'Updated Name', phoneNumber: '+1-555-0100' },
    });
    expect(fixture.componentInstance.profile()).toEqual(updated);
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('save() sends null for a field the operator clears entirely', async () => {
    const fixture = await createComponent();
    const cleared = { ...initialProfile, displayName: null };
    usersApiSpy.apiUsersMePatch.and.returnValue(of(cleared));
    fixture.componentInstance.profileForm.controls.displayName.setValue('   ');

    await fixture.componentInstance.save();

    expect(usersApiSpy.apiUsersMePatch).toHaveBeenCalledWith({
      body: { displayName: null, phoneNumber: null },
    });
  });

  it('save() resets the avatar-fallback flag on a successful save', async () => {
    const fixture = await createComponent();
    fixture.componentInstance.onAvatarError();
    expect(fixture.componentInstance.avatarFailed()).toBeTrue();
    usersApiSpy.apiUsersMePatch.and.returnValue(of(initialProfile));

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.avatarFailed()).toBeFalse();
  });

  it('save() shows an error message on failure and does not clear saving forever', async () => {
    const fixture = await createComponent();
    usersApiSpy.apiUsersMePatch.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: 'Bad request.' })),
    );

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('Bad request.');
    expect(fixture.componentInstance.saving()).toBeFalse();
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent();
    fixture.componentInstance.profileForm.controls.displayName.setValue('x'.repeat(300));

    await fixture.componentInstance.save();

    expect(usersApiSpy.apiUsersMePatch).not.toHaveBeenCalled();
  });

  it('changeEmail() posts the new address and the current password', async () => {
    const fixture = await createComponent();
    usersApiSpy.apiUsersMeEmailPost.and.returnValue(
      of(new HttpResponse({ status: 202, body: { confirmationSent: true } })),
    );
    fixture.componentInstance.changeEmailForm.setValue({
      newEmail: 'new@example.com',
      currentPassword: 'Old-Password-1234!',
    });

    await fixture.componentInstance.changeEmail();

    expect(usersApiSpy.apiUsersMeEmailPost).toHaveBeenCalledWith(
      {
        changeEmailRequest: { newEmail: 'new@example.com', currentPassword: 'Old-Password-1234!' },
      },
      'response',
    );
  });

  it('changeEmail() on a 202 asks the user to check the inbox and keeps the session', async () => {
    const fixture = await createComponent();
    usersApiSpy.apiUsersMeEmailPost.and.returnValue(
      of(new HttpResponse({ status: 202, body: { confirmationSent: true } })),
    );
    fixture.componentInstance.changeEmailForm.setValue({
      newEmail: 'new@example.com',
      currentPassword: 'Old-Password-1234!',
    });

    await fixture.componentInstance.changeEmail();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Check the inbox of new@example.com',
    );
    expect(authServiceSpy.endSessionAfterSecurityChange).not.toHaveBeenCalled();
    expect(routerSpy.navigateByUrl).not.toHaveBeenCalled();
  });

  it('changeEmail() on a 200 signs out with a message and opens the login page', async () => {
    const fixture = await createComponent();
    usersApiSpy.apiUsersMeEmailPost.and.returnValue(
      of(new HttpResponse({ status: 200, body: { ...initialProfile, email: 'new@example.com' } })),
    );
    fixture.componentInstance.changeEmailForm.setValue({
      newEmail: 'new@example.com',
      currentPassword: 'Old-Password-1234!',
    });

    await fixture.componentInstance.changeEmail();

    expect(authServiceSpy.endSessionAfterSecurityChange).toHaveBeenCalledWith(
      jasmine.stringContaining('Your e-mail address was changed'),
    );
    expect(routerSpy.navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('changeEmail() shows the problem detail of a 400 and stays signed in', async () => {
    const fixture = await createComponent();
    usersApiSpy.apiUsersMeEmailPost.and.returnValue(
      throwError(
        () => new HttpErrorResponse({ status: 400, error: { detail: 'The password is wrong.' } }),
      ),
    );
    fixture.componentInstance.changeEmailForm.setValue({
      newEmail: 'new@example.com',
      currentPassword: 'nope',
    });

    await fixture.componentInstance.changeEmail();

    expect(fixture.componentInstance.changeEmailErrorMessage()).toBe('The password is wrong.');
    expect(authServiceSpy.endSessionAfterSecurityChange).not.toHaveBeenCalled();
  });

  it('changeEmail() does nothing while the address is not an e-mail address', async () => {
    const fixture = await createComponent();
    fixture.componentInstance.changeEmailForm.setValue({
      newEmail: 'not-an-address',
      currentPassword: 'x',
    });

    await fixture.componentInstance.changeEmail();

    expect(usersApiSpy.apiUsersMeEmailPost).not.toHaveBeenCalled();
  });

  it('says a link will be sent when the server requires confirmation, and that the change is immediate otherwise', async () => {
    const withLink = await createComponent(() =>
      usersApiSpy.apiUsersMeGet.and.returnValue(
        of({ ...initialProfile, emailChangeRequiresConfirmation: true }),
      ),
    );
    expect(
      (withLink.nativeElement as HTMLElement).querySelector('#email-change-mode')?.textContent,
    ).toContain('We send a link');
    TestBed.resetTestingModule();

    const immediate = await createComponent(() =>
      usersApiSpy.apiUsersMeGet.and.returnValue(
        of({ ...initialProfile, emailChangeRequiresConfirmation: false }),
      ),
    );
    expect(
      (immediate.nativeElement as HTMLElement).querySelector('#email-change-mode')?.textContent,
    ).toContain('changes as soon as you submit');
  });

  it('changePassword() sends the current/new password and resets the form on success', async () => {
    const fixture = await createComponent();
    usersApiSpy.apiUsersMeChangePasswordPost.and.returnValue(of(undefined));
    fixture.componentInstance.changePasswordForm.setValue({
      currentPassword: 'Old-Password-1234!',
      newPassword: 'New-Password-5678!',
      confirmNewPassword: 'New-Password-5678!',
    });

    await fixture.componentInstance.changePassword();

    expect(usersApiSpy.apiUsersMeChangePasswordPost).toHaveBeenCalledWith({
      changePasswordRequest: {
        currentPassword: 'Old-Password-1234!',
        newPassword: 'New-Password-5678!',
      },
    });
    expect(fixture.componentInstance.changePasswordForm.getRawValue()).toEqual({
      currentPassword: '',
      newPassword: '',
      confirmNewPassword: '',
    });
    // The server ended every session of the account, this one included.
    expect(authServiceSpy.endSessionAfterSecurityChange).toHaveBeenCalledWith(
      jasmine.stringContaining('Your password was changed'),
    );
    expect(routerSpy.navigateByUrl).toHaveBeenCalledWith('/login');
  });

  it('changePassword() stays on the page and keeps the session when the change fails', async () => {
    const fixture = await createComponent();
    usersApiSpy.apiUsersMeChangePasswordPost.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 400, error: 'Incorrect password.' })),
    );
    fixture.componentInstance.changePasswordForm.setValue({
      currentPassword: 'Wrong-Password-1234!',
      newPassword: 'New-Password-5678!',
      confirmNewPassword: 'New-Password-5678!',
    });

    await fixture.componentInstance.changePassword();

    expect(authServiceSpy.endSessionAfterSecurityChange).not.toHaveBeenCalled();
    expect(routerSpy.navigateByUrl).not.toHaveBeenCalled();
  });

  it('changePassword() surfaces the real backend error -- e.g. a wrong current password -- rather than a generic message', async () => {
    const fixture = await createComponent();
    usersApiSpy.apiUsersMeChangePasswordPost.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { errors: { PasswordMismatch: ['Incorrect password.'] } },
          }),
      ),
    );
    fixture.componentInstance.changePasswordForm.setValue({
      currentPassword: 'Wrong-Password-0000!',
      newPassword: 'New-Password-5678!',
      confirmNewPassword: 'New-Password-5678!',
    });

    await fixture.componentInstance.changePassword();

    expect(fixture.componentInstance.changePasswordErrorMessage()).toBe('Incorrect password.');
    expect(fixture.componentInstance.changingPassword()).toBeFalse();
  });

  it('changePassword() does nothing while the new password and confirmation do not match', async () => {
    const fixture = await createComponent();
    fixture.componentInstance.changePasswordForm.setValue({
      currentPassword: 'Old-Password-1234!',
      newPassword: 'New-Password-5678!',
      confirmNewPassword: 'Different-Password-9999!',
    });

    expect(fixture.componentInstance.changePasswordForm.invalid).toBeTrue();

    await fixture.componentInstance.changePassword();

    expect(usersApiSpy.apiUsersMeChangePasswordPost).not.toHaveBeenCalled();
  });

  it('changePassword() does nothing while a required field is empty', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.changePassword();

    expect(usersApiSpy.apiUsersMeChangePasswordPost).not.toHaveBeenCalled();
  });
});
