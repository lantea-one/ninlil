import { Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardHeaderComponent,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { AuthService } from '../../../core/auth/auth.service';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';

/**
 * This validator checks that `newPassword` and `confirmNewPassword` match, reporting the mismatch
 * on the group. `AccountProfileComponent` has the same small validator.
 */
function passwordsMatchValidator(control: AbstractControl): ValidationErrors | null {
  const newPassword = control.get('newPassword')?.value;
  const confirmNewPassword = control.get('confirmNewPassword')?.value;
  return newPassword === confirmNewPassword ? null : { passwordsMismatch: true };
}

/**
 * This component is the public "set new password" page
 * (`/reset-password?userId=...&token=...`). Two e-mails link here: the reset link an administrator
 * sends from the Users page (`EmailLink` delivery of `POST /api/users/{id}/reset-password`), and
 * the one from the Forgot Password page. Both are confirmed by the anonymous
 * `POST /api/auth/reset-password`; the page does not need to know which e-mail was used.
 *
 * <p>`userId` and `token` are read once from the route snapshot, since the page is never
 * re-navigated with other values. If either is missing, the page shows an invalid-link message
 * instead of a form that could only fail. A completed reset does not sign the user in.
 */
@Component({
  selector: 'app-set-new-password',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardHeaderComponent,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    IconDirective,
    SpinnerComponent,
  ],
  templateUrl: './set-new-password.component.html',
  styleUrl: './set-new-password.component.scss',
})
export class SetNewPasswordComponent {
  private readonly authService = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  /** The `userId` from the link, or `null` if missing. */
  private readonly userId = this.route.snapshot.queryParamMap.get('userId');

  /** The reset `token` from the link, or `null` if missing. */
  private readonly token = this.route.snapshot.queryParamMap.get('token');

  /** Whether the link carried both `userId` and `token`; without them no form is shown. */
  readonly hasValidLink = this.userId !== null && this.token !== null;

  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly completed = signal(false);

  /**
   * The minimum new-password length checked in the browser, the same as on the Profile page. The
   * server's password policy can be stricter; its errors are shown as they come back.
   */
  readonly passwordMinLength = 12;

  readonly form = this.formBuilder.group(
    {
      newPassword: ['', [Validators.required, Validators.minLength(this.passwordMinLength)]],
      confirmNewPassword: ['', [Validators.required]],
    },
    { validators: [passwordsMatchValidator] },
  );

  /**
   * This method submits the new password to `POST /api/auth/reset-password`. An error without a
   * readable message is shown as an expired link.
   */
  async submit(): Promise<void> {
    if (this.form.invalid || this.submitting() || !this.userId || !this.token) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      await this.authService.resetPassword(
        this.userId,
        this.token,
        this.form.getRawValue().newPassword,
      );
      this.completed.set(true);
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(
          error,
          'This password reset link is no longer valid. Request a new one.',
        ),
      );
    } finally {
      this.submitting.set(false);
    }
  }
}
