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

/** This interface describes the account whose password {@link ResetPasswordDialogComponent} resets. */
export interface ResetPasswordDialogTarget {
  /** The account id. */
  id: string;

  /** The sign-in name, shown in the dialog title. */
  userName: string;

  /** The e-mail address, if any; shown with the e-mail link option, which is unavailable without one. */
  email: string | null | undefined;
}

/**
 * This component is the password reset dialog the Users page (`UserListComponent`) opens for an
 * account that cannot sign in. It calls `POST /api/users/{id}/reset-password` (`users:write`)
 * with one of two modes: generate a password and show it once in
 * {@link RevealSecretDialogComponent}, or e-mail the account a link to the `/reset-password` page
 * (`SetNewPasswordComponent`).
 *
 * <p>While the mode is being chosen, a backdrop click or Escape closes the dialog, since nothing
 * is lost yet. A generated password is shown in the reveal-once dialog, which cannot be closed by
 * accident.
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

  /** Whether the mode-selection step is shown; the caller drives this with a signal. */
  readonly visible = input<boolean>(false);

  /** The account whose password is reset. */
  readonly target = input<ResetPasswordDialogTarget | null>(null);

  /** Emits `false` whenever the mode step closes: a cancel, a backdrop click or Escape, or a successful reset. */
  readonly visibleChange = output<boolean>();

  /** Emits when the flow is finished: after the password dialog closes, or right after the link is sent. */
  readonly completed = output<void>();

  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly modeForm = this.formBuilder.group({
    mode: this.formBuilder.control<AdminPasswordResetMode>('AutoGenerate'),
  });

  /** Whether the reveal-once dialog is shown for a generated password. */
  readonly revealDialogVisible = signal(false);

  /** The generated password, shown once. */
  readonly revealDialogData = signal<RevealSecretDialogData | null>(null);

  readonly modeAutoGenerate: AdminPasswordResetMode = 'AutoGenerate';
  readonly modeEmailLink: AdminPasswordResetMode = 'EmailLink';

  /** This method resets the password with the chosen mode, then shows the password or reports the e-mail. */
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
        // Close this dialog and show the password in the reveal-once dialog.
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

  /** This method handles the mode step's modal closing itself (a backdrop click or Escape) as a cancel. */
  onModalVisibleChange(visible: boolean): void {
    if (!visible) {
      this.errorMessage.set(null);
      this.visibleChange.emit(false);
    }
  }

  /** This method hides the password dialog and finishes the flow. */
  onRevealDialogClosed(): void {
    this.revealDialogVisible.set(false);
    this.completed.emit();
  }
}
