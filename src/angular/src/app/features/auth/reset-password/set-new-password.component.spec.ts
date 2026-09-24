import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { SetNewPasswordComponent } from './set-new-password.component';

describe('SetNewPasswordComponent', () => {
  let fixture: ComponentFixture<SetNewPasswordComponent>;
  let authServiceSpy: { resetPassword: jasmine.Spy };

  async function createWith(queryParams: Record<string, string>): Promise<void> {
    authServiceSpy = jasmine.createSpyObj('AuthService', ['resetPassword']);

    await TestBed.configureTestingModule({
      imports: [SetNewPasswordComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: authServiceSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SetNewPasswordComponent);
    fixture.detectChanges();
  }

  it('reports a valid link, and shows the form, when both userId and token are present', async () => {
    await createWith({ userId: '11111111-1111-1111-1111-111111111111', token: 'abc123' });

    expect(fixture.componentInstance.hasValidLink).toBeTrue();
  });

  it('reports an invalid link when userId is missing', async () => {
    await createWith({ token: 'abc123' });

    expect(fixture.componentInstance.hasValidLink).toBeFalse();
  });

  it('reports an invalid link when token is missing', async () => {
    await createWith({ userId: '11111111-1111-1111-1111-111111111111' });

    expect(fixture.componentInstance.hasValidLink).toBeFalse();
  });

  it('reports an invalid link when neither query param is present', async () => {
    await createWith({});

    expect(fixture.componentInstance.hasValidLink).toBeFalse();
  });

  it('submit() resets the password with the link-supplied userId/token and marks the flow completed', async () => {
    await createWith({ userId: '11111111-1111-1111-1111-111111111111', token: 'abc123' });
    authServiceSpy.resetPassword.and.returnValue(Promise.resolve());
    fixture.componentInstance.form.setValue({
      newPassword: 'Brand-New-Password-5678!',
      confirmNewPassword: 'Brand-New-Password-5678!',
    });

    await fixture.componentInstance.submit();

    expect(authServiceSpy.resetPassword).toHaveBeenCalledWith(
      '11111111-1111-1111-1111-111111111111',
      'abc123',
      'Brand-New-Password-5678!',
    );
    expect(fixture.componentInstance.completed()).toBeTrue();
  });

  it('submit() shows an error message on failure -- e.g. an expired/invalid token', async () => {
    await createWith({ userId: '11111111-1111-1111-1111-111111111111', token: 'abc123' });
    authServiceSpy.resetPassword.and.returnValue(Promise.reject(new Error('rejected')));
    fixture.componentInstance.form.setValue({
      newPassword: 'Brand-New-Password-5678!',
      confirmNewPassword: 'Brand-New-Password-5678!',
    });

    await fixture.componentInstance.submit();

    expect(fixture.componentInstance.errorMessage()).toBe(
      'This password reset link is no longer valid. Request a new one.',
    );
    expect(fixture.componentInstance.completed()).toBeFalse();
    expect(fixture.componentInstance.submitting()).toBeFalse();
  });

  it('submit() does nothing while the new password and confirmation do not match', async () => {
    await createWith({ userId: '11111111-1111-1111-1111-111111111111', token: 'abc123' });
    fixture.componentInstance.form.setValue({
      newPassword: 'Brand-New-Password-5678!',
      confirmNewPassword: 'Something-Else-9999!',
    });

    expect(fixture.componentInstance.form.invalid).toBeTrue();

    await fixture.componentInstance.submit();

    expect(authServiceSpy.resetPassword).not.toHaveBeenCalled();
  });

  it('submit() does nothing when the link is missing userId/token, even if the form is somehow valid', async () => {
    await createWith({});
    fixture.componentInstance.form.setValue({
      newPassword: 'Brand-New-Password-5678!',
      confirmNewPassword: 'Brand-New-Password-5678!',
    });

    await fixture.componentInstance.submit();

    expect(authServiceSpy.resetPassword).not.toHaveBeenCalled();
  });
});
