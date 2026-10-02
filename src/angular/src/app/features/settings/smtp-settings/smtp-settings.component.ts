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
import { SmtpSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { buildSmtpSettingsPatchBody } from './smtp-settings.model';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "SMTP" settings page (`/settings/smtp`). It edits `EmailSenderOptions`,
 * the SMTP server used for e-mailed two-factor codes and password reset links, through `GET` and
 * `PATCH /api/smtp-settings` (`smtpsettings:read`, `smtpsettings:write`).
 *
 * <p>Every field is hot-reloaded: each send opens a new connection, and `SmtpEmailSender` reads the
 * options for every send.
 *
 * <p>The password is never returned, so a blank password field means "keep the stored one" (see
 * {@link buildSmtpSettingsPatchBody}).
 */
@Component({
  selector: 'app-smtp-settings',
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
    SpinnerComponent,
  ],
  templateUrl: './smtp-settings.component.html',
  styleUrl: './smtp-settings.component.scss',
})
export class SmtpSettingsComponent implements OnInit {
  private readonly smtpApi = inject(SmtpSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** Whether a password is stored, per the last `Get`; shown as a hint, never as a value. */
  readonly passwordConfigured = signal(false);

  readonly form = this.formBuilder.group({
    smtpHost: ['', [Validators.required]],
    smtpPort: [587, [Validators.required, Validators.min(1), Validators.max(65535)]],
    useTls: [true],
    username: [''],
    password: [''],
    fromAddress: ['', [Validators.required, Validators.email]],
    fromDisplayName: ['Ninlil', [Validators.required]],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /**
   * This method PATCHes every field, leaving out a blank `password` (see
   * {@link buildSmtpSettingsPatchBody}), then shows the backend's note.
   */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();
    const body = buildSmtpSettingsPatchBody(value);

    try {
      const result = await firstValueFrom(this.smtpApi.apiSmtpSettingsPatch({ body }));
      this.notification.success(result.note ?? 'SMTP settings saved.');

      if (value.password.trim().length > 0) this.passwordConfigured.set(true);

      // The password is never returned; clearing the field keeps "blank means unchanged" true.
      this.form.patchValue({ password: '' });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save SMTP settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.smtpApi.apiSmtpSettingsGet());
      this.passwordConfigured.set(settings.passwordConfigured ?? false);
      this.form.patchValue({
        smtpHost: settings.smtpHost ?? '',
        smtpPort: settings.smtpPort ?? 587,
        useTls: settings.useTls ?? true,
        username: settings.username ?? '',
        fromAddress: settings.fromAddress ?? '',
        fromDisplayName: settings.fromDisplayName ?? 'Ninlil',
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load SMTP settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
