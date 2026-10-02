import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  AlertComponent,
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
  FormTextDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { IdentitySettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "Identity" settings page (`/settings/identity`). It edits the ASP.NET Core
 * Identity password and account lockout policy through `GET` and `PATCH /api/identity-settings`
 * (`identitysettings:read`, `identitysettings:write`).
 * <p>A change applies from the next sign-in, registration or password change, not to requests
 * already running (see `IdentitySettingsController`). The backend's note is shown on save.
 */
@Component({
  selector: 'app-identity-settings',
  imports: [
    IfPermissionDirective,
    ReactiveFormsModule,
    AlertComponent,
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
    FormTextDirective,
    SpinnerComponent,
  ],
  templateUrl: './identity-settings.component.html',
  styleUrl: './identity-settings.component.scss',
})
export class IdentitySettingsComponent implements OnInit {
  private readonly identityApi = inject(IdentitySettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    requiredLength: [12, [Validators.required, Validators.min(1)]],
    requiredUniqueChars: [1, [Validators.required, Validators.min(1)]],
    requireDigit: [true],
    requireLowercase: [true],
    requireUppercase: [true],
    requireNonAlphanumeric: [true],
    defaultLockoutTimeSpan: ['00:15:00', [Validators.required]],
    maxFailedAccessAttempts: [5, [Validators.required, Validators.min(1)]],
    allowedForNewUsers: [true],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes every field, then shows the backend's note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.identityApi.apiIdentitySettingsPatch({ body: this.form.getRawValue() }),
      );
      this.notification.success(result.note ?? 'Identity settings saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save identity settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.identityApi.apiIdentitySettingsGet());
      this.form.patchValue({
        requiredLength: settings.requiredLength ?? 12,
        requiredUniqueChars: settings.requiredUniqueChars ?? 1,
        requireDigit: settings.requireDigit ?? true,
        requireLowercase: settings.requireLowercase ?? true,
        requireUppercase: settings.requireUppercase ?? true,
        requireNonAlphanumeric: settings.requireNonAlphanumeric ?? true,
        defaultLockoutTimeSpan: settings.defaultLockoutTimeSpan ?? '00:15:00',
        maxFailedAccessAttempts: settings.maxFailedAccessAttempts ?? 5,
        allowedForNewUsers: settings.allowedForNewUsers ?? true,
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load identity settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
