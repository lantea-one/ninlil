import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  CardHeaderComponent,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  FormTextDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { NotifySettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "NOTIFY" settings page (`/settings/notify`). It edits `NotifyOptions`
 * through `GET` and `PATCH /api/notify-settings` (`notifysettings:read`, `notifysettings:write`):
 * how many times, how often and to which port the server sends RFC 1996 NOTIFY messages to a
 * zone's secondaries. Changes are hot-reloaded.
 */
@Component({
  selector: 'app-notify-settings',
  imports: [
    IfPermissionDirective,
    ReactiveFormsModule,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    FormTextDirective,
    SpinnerComponent,
  ],
  templateUrl: './notify-settings.component.html',
  styleUrl: './notify-settings.component.scss',
})
export class NotifySettingsComponent implements OnInit {
  private readonly notifyApi = inject(NotifySettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    maxAttempts: [5, [Validators.required, Validators.min(1)]],
    responseTimeout: ['00:00:03', [Validators.required]],
    retryDelay: ['00:00:03', [Validators.required]],
    secondaryPort: [53, [Validators.required, Validators.min(1), Validators.max(65535)]],
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
        this.notifyApi.apiNotifySettingsPatch({ body: this.form.getRawValue() }),
      );
      this.notification.success(result.note ?? 'NOTIFY settings saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save NOTIFY settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.notifyApi.apiNotifySettingsGet());
      this.form.patchValue({
        maxAttempts: settings.maxAttempts ?? 5,
        responseTimeout: settings.responseTimeout ?? '00:00:03',
        retryDelay: settings.retryDelay ?? '00:00:03',
        secondaryPort: settings.secondaryPort ?? 53,
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load NOTIFY settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
