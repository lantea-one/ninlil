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

/**
 * This component reads and writes this host's own `EmailSenderOptions` -- the SMTP server that
 * already, genuinely delivers e-mail-based two-factor authentication codes but, before this page
 * existed, had zero admin-facing configuration surface, via `GET`/`PATCH /api/smtp-settings`
 * (`smtpsettings:read`/`smtpsettings:write`). This is the prerequisite a future self-service
 * Forgot Password flow needs, since sending a real reset-token e-mail needs a real,
 * admin-configured SMTP server to send it through.
 *
 * <p>Every field this form edits takes effect immediately, with no restart -- unlike a transport
 * listener's own bound socket, an SMTP send opens a brand new connection every single time, so
 * `SmtpEmailSender` reads its own configuration fresh on every send via `IOptionsMonitor`. The
 * backend's own `PATCH` response note (surfaced directly in the success notification, never
 * suppressed) confirms this explicitly.
 *
 * <p>Like {@link EventBrokerSettingsComponent}'s own broker password fields, `password` here is a
 * genuine secret this page can never pre-fill (`SmtpSettingsController.Get` never returns it) --
 * leaving it blank on save means "leave the stored password unchanged," never "clear it." See
 * {@link buildSmtpSettingsPatchBody}'s own doc comment for the full reasoning.
 */
@Component({
  selector: 'app-smtp-settings',
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

  /** Whether a password is currently on file, per the last `Get` -- shown as a hint, never as a value. */
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
   * This method PATCHes every field this form holds, omitting `password` entirely when left
   * blank (see {@link buildSmtpSettingsPatchBody}), then surfaces the backend's own hot-reload
   * note.
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

      // Never re-populate the password field from the response -- it is never returned, so
      // clearing it back to blank keeps this form's own "blank means unchanged" contract honest.
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
