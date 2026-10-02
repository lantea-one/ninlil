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
import { WebhookDeliverySettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "Webhook Delivery" settings page (`/settings/webhook-delivery`). It edits
 * `WebhookDeliveryOptions` through `GET` and `PATCH /api/webhook-delivery-settings`
 * (`webhookdeliverysettings:read`, `webhookdeliverysettings:write`): the retry backoff and HTTP
 * timeout shared by every webhook delivery. Each subscription's `MaxDeliveryAttempts` is set on
 * the subscription itself. The retry delays apply to the next attempt; `requestTimeout` applies
 * when the HTTP handler is next rebuilt (about every two minutes). The backend's note says which
 * applies.
 */
@Component({
  selector: 'app-webhook-delivery-settings',
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
  templateUrl: './webhook-delivery-settings.component.html',
  styleUrl: './webhook-delivery-settings.component.scss',
})
export class WebhookDeliverySettingsComponent implements OnInit {
  private readonly webhookDeliveryApi = inject(WebhookDeliverySettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    initialRetryDelay: ['00:00:01', [Validators.required]],
    maxRetryDelay: ['00:00:30', [Validators.required]],
    requestTimeout: ['00:00:10', [Validators.required]],
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
        this.webhookDeliveryApi.apiWebhookDeliverySettingsPatch({ body: this.form.getRawValue() }),
      );
      this.notification.success(result.note ?? 'Webhook delivery settings saved.');
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not save webhook delivery settings.'),
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
        this.webhookDeliveryApi.apiWebhookDeliverySettingsGet(),
      );
      this.form.patchValue({
        initialRetryDelay: settings.initialRetryDelay ?? '00:00:01',
        maxRetryDelay: settings.maxRetryDelay ?? '00:00:30',
        requestTimeout: settings.requestTimeout ?? '00:00:10',
      });
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not load webhook delivery settings.'),
      );
    } finally {
      this.loading.set(false);
    }
  }
}
