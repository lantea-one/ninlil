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

/**
 * This component reads and writes this host's own `WebhookDeliveryOptions` (`GET`/`PATCH
 * /api/webhook-delivery-settings`) -- the shared retry/backoff timing and outbound HTTP timeout
 * every webhook subscription's own deliveries use. Deliberately separate from
 * `WebhookSubscriptionsController`'s own per-subscription `MaxDeliveryAttempts`. No secret
 * material is involved, so this is a plain read/PATCH-with-current-value round trip -- built from
 * {@link ZoneSigningSettingsComponent}'s own reference pattern. `requestTimeout` hot-reloads on a
 * slightly different schedule than the two retry-delay fields (the very next outbound HTTP
 * handler build, roughly every two minutes, not the very next delivery attempt) -- the backend's
 * own returned note names exactly which applies to a given save, so this page surfaces it
 * unconditionally rather than guessing.
 */
@Component({
  selector: 'app-webhook-delivery-settings',
  imports: [
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

  /** This method PATCHes every field this form holds, then surfaces the backend's own note (which one of its three cases applies depends on which fields changed). */
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
