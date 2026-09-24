import { Component, inject, input, output, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import {
  AlertComponent,
  ButtonDirective,
  FormCheckComponent,
  FormCheckInputDirective,
  FormCheckLabelDirective,
  ModalBodyComponent,
  ModalComponent,
  ModalFooterComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { AdminPasswordResetMode, UsersService } from '../../../api-client';
import {
  RevealSecretDialogComponent,
  RevealSecretDialogData,
} from '../../../shared/reveal-secret-dialog/reveal-secret-dialog.component';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';

/** This interface describes which account {@link ResetPasswordDialogComponent} is currently resetting the password for. */
export interface ResetPasswordDialogTarget {
  /** The account's own identifier. */
  id: string;

  /** The account's sign-in name, shown in this dialog's own title. */
  userName: string;

  /** The account's e-mail address, if any -- shown alongside the "email link" delivery option, and used to explain why that option is unavailable when absent. */
  email: string | null | undefined;
}

/**
 * This component is the administrator-driven password-reset dialog `UserListComponent` opens for
 * a locked-out account, wired to `POST /api/users/{id}/reset-password`
 * (`UsersController.ResetPassword`). It offers the same two delivery modes that endpoint itself
 * supports (see `AdminPasswordResetMode`'s own backend doc comment for the full reasoning):
 * auto-generate a strong password and reveal it exactly once (via the shared
 * {@link RevealSecretDialogComponent}, this project's established reveal-once secret UI, reused
 * unchanged rather than re-built), or e-mail the account a real password-reset link to this
 * project's shared "set new password" landing page (`SetNewPasswordComponent`, at
 * `/reset-password`).
 *
 * Unlike {@link RevealSecretDialogComponent}/`ConfirmDialogComponent`, this dialog's own backdrop
 * and Escape key are left enabled while the mode-selection step is showing (nothing irreplaceable
 * is at stake yet) -- but once a generated password is being revealed, this component defers
 * entirely to `RevealSecretDialogComponent`'s own non-dismissible modal for that step, so the same
 * "cannot lose an unsaved secret to an accidental dismiss" guarantee still applies.
 */
@Component({
  selector: 'app-reset-password-dialog',
  imports: [
    ReactiveFormsModule,
    AlertComponent,
    ButtonDirective,
    FormCheckComponent,
    FormCheckInputDirective,
    FormCheckLabelDirective,
    ModalBodyComponent,
    ModalComponent,
    ModalFooterComponent,
    ModalHeaderComponent,
    ModalTitleDirective,
    SpinnerComponent,
    RevealSecretDialogComponent,
  ],
  templateUrl: './reset-password-dialog.component.html',
  styleUrl: './reset-password-dialog.component.scss',
})
export class ResetPasswordDialogComponent {
  private readonly usersApi = inject(UsersService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  /** Whether this dialog's own mode-selection step is currently shown -- a caller drives this with its own signal. */
  readonly visible = input<boolean>(false);

  /** Which account this dialog instance resets the password for. */
  readonly target = input<ResetPasswordDialogTarget | null>(null);

  /** Emits this dialog's own new visibility -- `false` on every close, whether cancelled, a delivery failure the operator gave up on, or (for the email-link mode) a completed send. */
  readonly visibleChange = output<boolean>();

  /** Emits once this whole flow is fully done and dismissed -- after the operator closes the reveal-once password dialog (auto-generate mode), or immediately after a successful send (email-link mode). A caller needs no payload here; nothing this dialog did changes what a caller should refresh. */
  readonly completed = output<void>();

  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly modeForm = this.formBuilder.group({
    mode: this.formBuilder.control<AdminPasswordResetMode>('AutoGenerate'),
  });

  /** Whether {@link RevealSecretDialogComponent} is currently shown for a just-generated password. */
  readonly revealDialogVisible = signal(false);

  /** The just-generated password, shown exactly once. */
  readonly revealDialogData = signal<RevealSecretDialogData | null>(null);

  readonly modeAutoGenerate: AdminPasswordResetMode = 'AutoGenerate';
  readonly modeEmailLink: AdminPasswordResetMode = 'EmailLink';

  /** This method submits the selected delivery mode against `POST /api/users/{id}/reset-password`. */
  async submit(): Promise<void> {
    const target = this.target();
    if (!target || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.usersApi.apiUsersIdResetPasswordPost({
          id: target.id,
          adminResetPasswordRequest: { mode: this.modeForm.getRawValue().mode },
        }),
      );

      if (result.mode === this.modeAutoGenerate && result.generatedPassword) {
        // Hand off to the shared reveal-once dialog -- this dialog's own visibility closes now,
        // the reveal dialog's own non-dismissible modal takes over from here.
        this.visibleChange.emit(false);
        this.revealDialogData.set({
          title: `Password reset for ${target.userName}`,
          message:
            'This password is shown once and never again. Save it now and share it with the ' +
            'account holder through a secure channel.',
          secrets: [{ label: 'New password', value: result.generatedPassword }],
        });
        this.revealDialogVisible.set(true);
      } else {
        this.visibleChange.emit(false);
        this.notification.success(`Password reset link sent to ${target.email}.`);
        this.completed.emit();
      }
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not reset this password.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /** This method runs when the mode-selection step's own modal reports a new visibility on its own (a backdrop click or Escape). */
  onModalVisibleChange(visible: boolean): void {
    if (!visible) {
      this.errorMessage.set(null);
      this.visibleChange.emit(false);
    }
  }

  /** This method runs once the operator dismisses the reveal-once password dialog, completing this whole flow. */
  onRevealDialogClosed(): void {
    this.revealDialogVisible.set(false);
    this.completed.emit();
  }
}
