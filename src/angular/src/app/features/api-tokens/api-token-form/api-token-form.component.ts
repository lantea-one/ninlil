import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  CardHeaderComponent,
  FormCheckComponent,
  FormCheckInputDirective,
  FormCheckLabelDirective,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { ApiTokensService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import {
  RevealSecretDialogComponent,
  RevealSecretDialogData,
} from '../../../shared/reveal-secret-dialog/reveal-secret-dialog.component';

/**
 * This component is the New API Token page (`/api-tokens/new`). It creates a token for the
 * signed-in user through `POST /api/api-tokens`, which needs only a session. A token cannot be
 * edited after creation, only revoked from the list, so there is no edit mode.
 *
 * <p>The response carries the raw token, which is never returned again. The page shows it in
 * {@link RevealSecretDialogComponent} and returns to the list once the dialog is closed.
 */
@Component({
  selector: 'app-api-token-form',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    FormCheckComponent,
    FormCheckInputDirective,
    FormCheckLabelDirective,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    SpinnerComponent,
    RevealSecretDialogComponent,
  ],
  templateUrl: './api-token-form.component.html',
  styleUrl: './api-token-form.component.scss',
})
export class ApiTokenFormComponent {
  private readonly router = inject(Router);
  private readonly apiTokensApi = inject(ApiTokensService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** Whether the reveal-once dialog is shown; set after a successful create. */
  readonly revealDialogVisible = signal(false);

  /** The new token's raw value, shown once in {@link RevealSecretDialogComponent}. */
  readonly revealDialogData = signal<RevealSecretDialogData | null>(null);

  readonly form = this.formBuilder.group({
    name: ['', [Validators.required]],
    oneTime: [false],
    expiresAtUtc: [''],
  });

  /** This method creates the token and shows its raw value. The expiry is sent as UTC. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      const created = await firstValueFrom(
        this.apiTokensApi.apiApiTokensPost({
          createApiTokenRequest: {
            name: value.name,
            oneTime: value.oneTime,
            expiresAtUtc:
              value.expiresAtUtc.trim().length > 0
                ? new Date(value.expiresAtUtc).toISOString()
                : null,
          },
        }),
      );

      // Stay on the page until the dialog is closed (onSecretDialogClosed).
      this.revealDialogData.set({
        title: 'API token created',
        message:
          'Copy this token now -- it will never be shown again. Configure it in whatever machine-to-machine caller will use it to authenticate.',
        secrets: [{ label: 'Token', value: created.rawToken ?? '' }],
      });
      this.revealDialogVisible.set(true);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not create this token.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /** This method hides the dialog and returns to the token list. */
  onSecretDialogClosed(): void {
    this.revealDialogVisible.set(false);
    void this.router.navigate(['/api-tokens']);
  }
}
