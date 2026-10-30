import { Component, OnInit, inject, signal } from '@angular/core';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
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
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { IdentitySettingsService } from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/** The largest `requiredLength` the API accepts (`IdentitySettingsController`). */
export const MAX_REQUIRED_LENGTH = 128;

/** The largest `requiredUniqueChars` the API accepts (`IdentitySettingsController`). */
export const MAX_REQUIRED_UNIQUE_CHARS = 64;

/**
 * This validator rejects a form whose `requiredUniqueChars` exceeds `requiredLength`, since no
 * password could meet both. It sets the `uniqueExceedsLength` error on the group, with the
 * message the API returns for the same mistake.
 */
export function uniqueCharsWithinLength(group: AbstractControl): ValidationErrors | null {
  const length = group.get('requiredLength')?.value;
  const unique = group.get('requiredUniqueChars')?.value;
  if (typeof length !== 'number' || typeof unique !== 'number') return null;
  return unique > length
    ? {
        uniqueExceedsLength: `'requiredUniqueChars' (${unique}) must not exceed 'requiredLength' (${length}).`,
      }
    : null;
}

/**
 * This component is the "Identity" settings page (`/settings/identity`). It edits the ASP.NET Core
 * Identity password and account lockout policy through `GET` and `PATCH /api/identity-settings`
 * (`identitysettings:read`, `identitysettings:write`).
 * <p>The page edits the password rules only. The API also carries the lockout fields
 * (`defaultLockoutTimeSpan`, `maxFailedAccessAttempts`, `allowedForNewUsers`), but sign-in does
 * not count failed passwords (`AuthController` signs in with `lockoutOnFailure: false`), so the
 * form does not offer them and the PATCH omits them. A note points to the admin login rate limit
 * page (`/settings/admin-login-rate-limit`), which bounds password guessing; it links there only
 * with `adminloginratelimit:read`, the permission that route needs.
 * <p>A change applies from the next sign-in, registration or password change, not to requests
 * already running (see `IdentitySettingsController`). The backend's note is shown on save.
 */
@Component({
  selector: 'app-identity-settings',
  imports: [
    RouterLink,
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
  private readonly auth = inject(AuthService);

  /** Whether the user may open the admin login rate limit page (`adminloginratelimit:read`). */
  readonly canOpenRateLimitPage = this.auth.hasPermission('adminloginratelimit:read');

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group(
    {
      requiredLength: [
        12,
        [Validators.required, Validators.min(1), Validators.max(MAX_REQUIRED_LENGTH)],
      ],
      requiredUniqueChars: [
        1,
        [Validators.required, Validators.min(1), Validators.max(MAX_REQUIRED_UNIQUE_CHARS)],
      ],
      requireDigit: [true],
      requireLowercase: [true],
      requireUppercase: [true],
      requireNonAlphanumeric: [true],
    },
    { validators: uniqueCharsWithinLength },
  );

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes every password field, then shows the backend's note. */
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
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load identity settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
