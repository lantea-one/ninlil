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
 * This validator checks that this page's own `newPassword`/`confirmNewPassword` fields hold the
 * same value, reporting the mismatch on the group itself, mirroring
 * `AccountProfileComponent`'s own identical validator for its change-password section (kept as
 * its own small, local copy rather than a shared import -- nothing else about these two forms is
 * shared, and this is the only piece either would need from the other).
 */
function passwordsMatchValidator(control: AbstractControl): ValidationErrors | null {
  const newPassword = control.get('newPassword')?.value;
  const confirmNewPassword = control.get('confirmNewPassword')?.value;
  return newPassword === confirmNewPassword ? null : { passwordsMismatch: true };
}

/**
 * This component is Ninlil's shared, public "set new password" landing page, reachable at
 * `/reset-password?userId=...&token=...` with no sign-in required (a top-level route sibling to
 * `AppShellComponent`, mirroring `LoginComponent`'s own route shape exactly -- see
 * `app.routes.ts`). It exists to serve two entry points that both need the exact same landing
 * page and confirming backend action (`AuthController.ResetPassword`):
 *
 * - The `EmailLink` delivery mode of `UsersController.ResetPassword` (administrator-driven reset
 *   for a locked-out account) e-mails a link to this exact page.
 * - `ForgotPasswordComponent`'s own self-service request flow, reachable from the login page,
 *   e-mails a link to this same page too -- this page was built generic enough (it neither knows
 *   nor cares which flow produced its `userId`/`token` query params) to need no changes at all to
 *   serve both.
 *
 * `userId`/`token` are read once, from this page's own initial navigation (`ActivatedRoute`'s
 * `snapshot`, mirroring `LoginComponent.navigateAfterSignIn`'s own `returnUrl` read) -- this page
 * is never navigated to again in place with different query params, so there is no need for the
 * reactive `queryParamMap` observable instead. A missing/malformed pair is treated as an
 * immediately invalid link, with no form shown at all, rather than letting the operator fill out
 * a form doomed to fail against `AuthController.ResetPassword`'s own real validation.
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

  /** The account identifier this page's own link carries, or `null` for a malformed/missing link. */
  private readonly userId = this.route.snapshot.queryParamMap.get('userId');

  /** The reset token this page's own link carries, or `null` for a malformed/missing link. */
  private readonly token = this.route.snapshot.queryParamMap.get('token');

  /** Whether this page's own link carried both a `userId` and a `token` -- when `false`, no form is shown at all. */
  readonly hasValidLink = this.userId !== null && this.token !== null;

  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly completed = signal(false);

  /**
   * This constant is the client-side floor {@link form}'s own `newPassword` field enforces before
   * ever submitting, mirroring `AccountProfileComponent`'s own identical constant for its
   * change-password section (see that class's own doc comment for why this floor is independent
   * of this server's admin-configurable password-complexity policy).
   */
  readonly passwordMinLength = 12;

  readonly form = this.formBuilder.group(
    {
      newPassword: ['', [Validators.required, Validators.minLength(this.passwordMinLength)]],
      confirmNewPassword: ['', [Validators.required]],
    },
    { validators: [passwordsMatchValidator] },
  );

  /** This method submits the new password against `POST /api/auth/reset-password`. */
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
