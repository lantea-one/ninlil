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
 * This component is the current caller's own self-service account-security page -- reachable
 * from the app shell's own user menu, at `/account/security`, gated only by `authGuard` (any
 * signed-in session), since `TwoFactorController` itself carries no `[RequirePermission(...)]`
 * at all: every action here acts on the caller's own account, never anyone else's. It covers
 * authenticator (TOTP) setup, disabling two-factor, and requesting an e-mailed verification code
 * -- everything `TwoFactorController` exposes.
 *
 * The page's own real enrollment state is read from the `GET .../two-factor/status` endpoint
 * rather than assumed from any locally-decoded token claim.
 *
 * This page's own "Recovery codes" section generates a batch and reveals all 10 codes exactly
 * once via the shared {@link RevealSecretDialogComponent} (one row per code), the same
 * reveal-once pattern every other secret in this project already follows -- see
 * `TsigKeyFormComponent` for the precedent this mirrors, including the declarative
 * (`[visible]`/`[data]`/`(closed)`) embedding rather than an imperative dialog-service call. Only
 * `status().recoveryCodesRemaining` is ever knowable again afterward, matching
 * `TwoFactorController.GetStatus`'s own deliberate omission of the actual code values from every
 * response but the one that just generated them.
 *
 * This page mounts no "Appearance" card -- the palette/mode switcher lives in its real, permanent
 * home in the shell's own header (`layout/app-shell/`), reachable from every page.
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

  /** The just-generated (not yet confirmed) authenticator enrollment, if setup was started. */
  readonly pendingSetup = signal<AuthenticatorSetupResult | null>(null);

  readonly settingUp = signal(false);
  readonly confirming = signal(false);
  readonly disabling = signal(false);
  readonly sendingEmailCode = signal(false);
  readonly generatingRecoveryCodes = signal(false);

  /** Whether {@link RevealSecretDialogComponent} is currently shown -- set once, right after a successful recovery-code generation. */
  readonly revealDialogVisible = signal(false);

  /** The just-generated batch of recovery codes, shown exactly once via {@link RevealSecretDialogComponent}. */
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

  /** This method starts (or restarts) authenticator enrollment. */
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

  /** This method confirms a just-generated authenticator enrollment with a produced code. */
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

  /** This method turns off the requirement for a second factor at sign-in. */
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
   * This method generates a fresh batch of 10 recovery codes, replacing (invalidating) any
   * previous batch, and reveals them exactly once via {@link RevealSecretDialogComponent} --
   * this account's own two-factor status is reloaded once the operator dismisses the dialog (see
   * {@link onSecretDialogClosed}), so the displayed remaining count reflects the fresh batch
   * immediately.
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

  /** This method runs once the operator dismisses the reveal-once recovery-codes dialog, reloading this account's own status only now that they have confirmed saving the codes. */
  async onSecretDialogClosed(): Promise<void> {
    this.revealDialogVisible.set(false);
    await this.loadStatus();
  }

  /** This method e-mails the current caller a one-time verification code. */
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
