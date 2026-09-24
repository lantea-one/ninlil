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
import { Dns64SettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';

/**
 * This component reads and writes this host's own `Dns64Options` (RFC 6147 DNS64) via
 * `GET`/`PATCH /api/dns64-settings`. No secret material is involved. Every field takes effect
 * immediately, with no restart, since the backend reads this options class live via
 * `IOptionsMonitor`. Off by default -- synthesizing an AAAA record nobody actually published is a
 * capability-expanding change, not a pure hardening one, so this form's own leading control is the
 * opt-in toggle itself, not a pre-enabled feature an operator merely tunes.
 */
@Component({
  selector: 'app-dns64-settings',
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
    FormTextDirective,
    SpinnerComponent,
  ],
  templateUrl: './dns64-settings.component.html',
  styleUrl: './dns64-settings.component.scss',
})
export class Dns64SettingsComponent implements OnInit {
  private readonly dns64Api = inject(Dns64SettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    enable: [false],
    synthesisPrefix: ['64:ff9b::/96', [Validators.required]],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes every field this form holds, then surfaces the backend's own hot-reload note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      const result = await firstValueFrom(this.dns64Api.apiDns64SettingsPatch({ body: value }));
      this.notification.success(result.note ?? 'DNS64 settings saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save DNS64 settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.dns64Api.apiDns64SettingsGet());
      this.form.patchValue({
        enable: settings.enable ?? false,
        synthesisPrefix: settings.synthesisPrefix ?? '64:ff9b::/96',
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load DNS64 settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
