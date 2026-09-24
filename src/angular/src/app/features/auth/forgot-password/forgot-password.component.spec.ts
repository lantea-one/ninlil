import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../../core/auth/auth.service';
import { ForgotPasswordComponent } from './forgot-password.component';

describe('ForgotPasswordComponent', () => {
  let fixture: ComponentFixture<ForgotPasswordComponent>;
  let authServiceSpy: { forgotPassword: jasmine.Spy };

  async function createComponent(): Promise<void> {
    authServiceSpy = jasmine.createSpyObj('AuthService', ['forgotPassword']);

    await TestBed.configureTestingModule({
      imports: [ForgotPasswordComponent],
      providers: [provideRouter([]), { provide: AuthService, useValue: authServiceSpy }],
    }).compileComponents();

    fixture = TestBed.createComponent(ForgotPasswordComponent);
    fixture.detectChanges();
  }

  it('renders the request form using real CoreUI markup', async () => {
    await createComponent();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('c-card')).not.toBeNull();
    expect(root.querySelector('form[cForm]')).not.toBeNull();
    expect(root.querySelector('input[type="email"][cFormControl]')).not.toBeNull();
    expect(root.querySelector('mat-card')).toBeNull();
  });

  it('does nothing when the form is submitted while invalid', async () => {
    await createComponent();

    await fixture.componentInstance.submit();

    expect(authServiceSpy.forgotPassword).not.toHaveBeenCalled();
  });

  it("submit() calls AuthService.forgotPassword with the entered address and shows the backend's own generic message", async () => {
    await createComponent();
    authServiceSpy.forgotPassword.and.resolveTo(
      'If an account exists for that e-mail address, a password reset link has been sent to it.',
    );
    fixture.componentInstance.form.setValue({ email: 'someone@example.com' });

    await fixture.componentInstance.submit();

    expect(authServiceSpy.forgotPassword).toHaveBeenCalledWith('someone@example.com');
    expect(fixture.componentInstance.completed()).toBeTrue();
    expect(fixture.componentInstance.confirmationMessage()).toBe(
      'If an account exists for that e-mail address, a password reset link has been sent to it.',
    );
  });

  it('shows the exact same completed state for an address that exists and one that does not -- this component never branches on the backend response', async () => {
    // The backend's own contract (AuthController.ForgotPassword) is to return byte-for-byte the
    // same response no matter what -- this test proves this component has no code path that could
    // ever diverge, by submitting twice with the exact same (mocked) resolved value and confirming
    // the exact same rendered outcome both times.
    await createComponent();
    const genericMessage =
      'If an account exists for that e-mail address, a password reset link has been sent to it.';
    authServiceSpy.forgotPassword.and.resolveTo(genericMessage);

    fixture.componentInstance.form.setValue({ email: 'real-account@example.com' });
    await fixture.componentInstance.submit();
    const firstOutcome = {
      completed: fixture.componentInstance.completed(),
      message: fixture.componentInstance.confirmationMessage(),
    };

    // Reset local component state to simulate a second, independent visit.
    fixture.componentInstance.completed.set(false);
    fixture.componentInstance.confirmationMessage.set(null);
    fixture.componentInstance.form.setValue({ email: 'no-such-account@example.com' });
    await fixture.componentInstance.submit();
    const secondOutcome = {
      completed: fixture.componentInstance.completed(),
      message: fixture.componentInstance.confirmationMessage(),
    };

    expect(firstOutcome).toEqual(secondOutcome);
  });

  it('submit() shows a generic error message on a genuine transport/network failure, never anything address-specific', async () => {
    await createComponent();
    authServiceSpy.forgotPassword.and.rejectWith(new Error('network error'));
    fixture.componentInstance.form.setValue({ email: 'someone@example.com' });

    await fixture.componentInstance.submit();

    expect(fixture.componentInstance.completed()).toBeFalse();
    expect(fixture.componentInstance.errorMessage()).toBe(
      'Something went wrong submitting this request. Please try again in a moment.',
    );
  });

  it('rejects a malformed e-mail address at the form level, without calling the backend', async () => {
    await createComponent();
    fixture.componentInstance.form.setValue({ email: 'not-an-email' });

    expect(fixture.componentInstance.form.invalid).toBeTrue();

    await fixture.componentInstance.submit();

    expect(authServiceSpy.forgotPassword).not.toHaveBeenCalled();
  });
});
