import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
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
import { AuthenticatorSetupResult, TwoFactorService, TwoFactorStatus } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import {
  RevealSecretDialogComponent,
  RevealSecretDialogData,
} from '../../../shared/reveal-secret-dialog/reveal-secret-dialog.component';

/**
 * This component is the signed-in user's Security page (`/account/security`), opened from the
 * account menu. It needs only a session: `TwoFactorController` (`/api/auth/two-factor`) acts on
 * the caller's account and requires no permission.
 *
 * <p>It shows the two-factor status from `GET .../status` and offers authenticator (TOTP) setup
 * and confirmation, turning two-factor off (which needs an authenticator code), new recovery
 * codes, and sending an e-mailed code when the account has a confirmed address.
 *
 * <p>Generating recovery codes replaces any earlier batch. The 10 new codes are shown once in
 * {@link RevealSecretDialogComponent}; after that only the number left is available.
 */
@Component({
  selector: 'app-account-security',
  imports: [
    ReactiveFormsModule,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardHeaderComponent,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    SpinnerComponent,
    IconDirective,
    RevealSecretDialogComponent,
  ],
  templateUrl: './account-security.component.html',
  styleUrl: './account-security.component.scss',
})
export class AccountSecurityComponent {
  private readonly twoFactorApi = inject(TwoFactorService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly status = signal<TwoFactorStatus | null>(null);
  readonly errorMessage = signal<string | null>(null);

  /** The authenticator enrollment that was started but not yet confirmed, if any. */
  readonly pendingSetup = signal<AuthenticatorSetupResult | null>(null);

  readonly settingUp = signal(false);
  readonly confirming = signal(false);
  readonly disabling = signal(false);
  readonly sendingEmailCode = signal(false);
  readonly generatingRecoveryCodes = signal(false);

  /** Whether the recovery-code dialog is shown; set after codes are generated. */
  readonly revealDialogVisible = signal(false);

  /** The new recovery codes, shown once in {@link RevealSecretDialogComponent}. */
  readonly revealDialogData = signal<RevealSecretDialogData | null>(null);

  readonly enableForm = this.formBuilder.group({
    code: ['', [Validators.required]],
  });

  readonly disableForm = this.formBuilder.group({
    code: ['', [Validators.required]],
  });

  constructor() {
    void this.loadStatus();
  }

  /** This method starts or restarts authenticator enrollment (`POST .../authenticator/setup`). */
  async startAuthenticatorSetup(): Promise<void> {
    this.settingUp.set(true);
    this.errorMessage.set(null);

    try {
      const setup = await firstValueFrom(
        this.twoFactorApi.apiAuthTwoFactorAuthenticatorSetupPost(),
      );
      this.pendingSetup.set(setup);
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not start authenticator setup.'));
    } finally {
      this.settingUp.set(false);
    }
  }

  /** This method confirms enrollment with a code from the authenticator app (`POST .../authenticator/enable`). */
  async confirmAuthenticatorSetup(): Promise<void> {
    if (this.enableForm.invalid || this.confirming()) return;

    this.confirming.set(true);
    this.errorMessage.set(null);

    try {
      await firstValueFrom(
        this.twoFactorApi.apiAuthTwoFactorAuthenticatorEnablePost({
          twoFactorCodeRequest: { code: this.enableForm.getRawValue().code },
        }),
      );
      this.pendingSetup.set(null);
      this.enableForm.reset();
      this.notification.success('Two-factor authentication enabled.');
      await this.loadStatus();
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'That code was not accepted.'));
    } finally {
      this.confirming.set(false);
    }
  }

  /** This method turns off two-factor sign-in (`POST .../disable`), which needs an authenticator code. */
  async disable(): Promise<void> {
    if (this.disableForm.invalid || this.disabling()) return;

    this.disabling.set(true);
    this.errorMessage.set(null);

    try {
      await firstValueFrom(
        this.twoFactorApi.apiAuthTwoFactorDisablePost({
          twoFactorCodeRequest: { code: this.disableForm.getRawValue().code },
        }),
      );
      this.disableForm.reset();
      this.notification.success('Two-factor authentication disabled.');
      await this.loadStatus();
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'That code was not accepted.'));
    } finally {
      this.disabling.set(false);
    }
  }

  /**
   * This method generates 10 new recovery codes (`POST .../recovery-codes/generate`), which
   * replace any earlier batch, and shows them once in {@link RevealSecretDialogComponent}. The
   * status, with its count of codes left, reloads when the dialog closes.
   */
  async generateRecoveryCodes(): Promise<void> {
    this.generatingRecoveryCodes.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.twoFactorApi.apiAuthTwoFactorRecoveryCodesGeneratePost(),
      );
      const codes = result.codes ?? [];

      this.revealDialogData.set({
        title: 'Recovery codes generated',
        message:
          'Copy these codes now -- they will never be shown again. Each one can be used exactly once, in place of an authenticator or e-mailed code, if you lose access to both. Generating a new batch immediately invalidates every code from any previous batch.',
        secrets: codes.map((code, index) => ({ label: `Code ${index + 1}`, value: code })),
      });
      this.revealDialogVisible.set(true);
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not generate recovery codes.'));
    } finally {
      this.generatingRecoveryCodes.set(false);
    }
  }

  /** This method hides the recovery-code dialog and reloads the status. */
  async onSecretDialogClosed(): Promise<void> {
    this.revealDialogVisible.set(false);
    await this.loadStatus();
  }

  /** This method e-mails the signed-in user a one-time code (`POST .../email/send`). */
  async sendEmailCode(): Promise<void> {
    this.sendingEmailCode.set(true);

    try {
      await firstValueFrom(this.twoFactorApi.apiAuthTwoFactorEmailSendPost());
      this.notification.success('A verification code was sent to your e-mail address.');
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not send a verification code.'));
    } finally {
      this.sendingEmailCode.set(false);
    }
  }

  private async loadStatus(): Promise<void> {
    this.loading.set(true);

    try {
      const status = await firstValueFrom(this.twoFactorApi.apiAuthTwoFactorStatusGet());
      this.status.set(status);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load your security status.'));
    } finally {
      this.loading.set(false);
    }
  }
}
