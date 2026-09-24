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
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { AdminLoginRateLimitSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';

/**
 * This component reads and writes this host's own `AdminLoginRateLimitOptions` -- the admin-login
 * brute-force limiter's own attempt threshold and lockout/window duration -- via
 * `GET`/`PATCH /api/admin-login-rate-limit-settings`, mirroring
 * `RateLimitingSettingsComponent`'s own exact shape. No secret material is involved here at all,
 * so every field is a plain read/PATCH-with-current-value round trip.
 *
 * <p>This is a genuinely different limiter from the "Rate limiting" tab elsewhere in this shell
 * (`DnsRateLimitOptions`, the DNS-query-path limiter): that one deliberately fails *open* past its
 * own cap, since DNS resolution itself must never become unavailable because of it, while this one
 * deliberately fails *closed* -- a login attempt past `permitLimit` within `window` is genuinely
 * rejected with `429`, not merely logged. Every successful `PATCH`'s own "restart required" note is
 * surfaced directly in the success notification, never suppressed.
 */
@Component({
  selector: 'app-admin-login-rate-limit-settings',
  imports: [
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
    SpinnerComponent,
  ],
  templateUrl: './admin-login-rate-limit-settings.component.html',
  styleUrl: './admin-login-rate-limit-settings.component.scss',
})
export class AdminLoginRateLimitSettingsComponent implements OnInit {
  private readonly adminLoginRateLimitApi = inject(AdminLoginRateLimitSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    enable: [true],
    permitLimit: [5, [Validators.required, Validators.min(1)]],
    window: ['00:05:00', [Validators.required]],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes every field this form holds, then surfaces the backend's own restart-required note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      const result = await firstValueFrom(
        this.adminLoginRateLimitApi.apiAdminLoginRateLimitSettingsPatch({ body: value }),
      );
      this.notification.success(result.note ?? 'Admin login rate limit settings saved.');
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not save admin login rate limit settings.'),
      );
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(
        this.adminLoginRateLimitApi.apiAdminLoginRateLimitSettingsGet(),
      );
      this.form.patchValue({
        enable: settings.enable ?? true,
        permitLimit: settings.permitLimit ?? 5,
        window: settings.window ?? '00:05:00',
      });
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not load admin login rate limit settings.'),
      );
    } finally {
      this.loading.set(false);
    }
  }
}
