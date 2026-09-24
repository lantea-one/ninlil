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
 * This component creates a new API token for the current caller -- create-only, unlike
 * `TsigKeyFormComponent`/`RoleFormComponent`, since `ApiTokensController` exposes no edit action
 * at all (a token's own name/type/expiry are fixed at issuance; only revoking it is possible
 * afterward, which `ApiTokenListComponent` already covers). On a successful create, this
 * component shows {@link RevealSecretDialogComponent} with the response's own `rawToken` -- the
 * one and only place this whole feature ever shows a token's own raw, secret value, matching
 * `ApiTokenSummary`'s own deliberate omission of it from every later `GET`/list response. The
 * dialog is embedded declaratively (`[visible]`/`[data]`/`(closed)`) rather than opened
 * imperatively, so navigation away from this page is deferred until the operator dismisses it --
 * see {@link RevealSecretDialogComponent}'s own doc comment for why.
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

  /** Whether {@link RevealSecretDialogComponent} is currently shown -- set once, right after a successful create. */
  readonly revealDialogVisible = signal(false);

  /** The just-issued token's own raw value, shown exactly once via {@link RevealSecretDialogComponent}. */
  readonly revealDialogData = signal<RevealSecretDialogData | null>(null);

  readonly form = this.formBuilder.group({
    name: ['', [Validators.required]],
    oneTime: [false],
    expiresAtUtc: [''],
  });

  /** This method issues the new token, then reveals its raw value exactly once. */
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

      // Reveal the raw token exactly once -- navigation away is deferred to
      // onSecretDialogClosed(), once the operator has actually dismissed the dialog.
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

  /** This method runs once the operator dismisses the reveal-once secret dialog, navigating away only now that they have confirmed saving the token. */
  onSecretDialogClosed(): void {
    this.revealDialogVisible.set(false);
    void this.router.navigate(['/api-tokens']);
  }
}
