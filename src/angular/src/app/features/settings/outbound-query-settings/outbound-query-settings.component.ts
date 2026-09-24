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
import { OutboundQuerySettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component reads and writes this host's own `OutboundQueryOptions` (UDP source-port
 * randomization/pooling for this server's own outbound recursive/forwarded queries) via
 * `GET`/`PATCH /api/outbound-query-settings`. No secret material is involved. Every field takes
 * effect immediately, with no restart, since the backend reads this options class live via
 * `IOptionsMonitor`.
 */
@Component({
  selector: 'app-outbound-query-settings',
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
  templateUrl: './outbound-query-settings.component.html',
  styleUrl: './outbound-query-settings.component.scss',
})
export class OutboundQuerySettingsComponent implements OnInit {
  private readonly outboundQueryApi = inject(OutboundQuerySettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    enableSourcePortPool: [true],
    poolSizePerAddressFamily: [128, [Validators.required, Validators.min(1)]],
    excludedSourcePortsText: [''],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes every field this form holds, then surfaces the backend's own hot-reload note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const { excludedSourcePortsText, ...value } = this.form.getRawValue();
    const body = {
      ...value,
      excludedSourcePorts: excludedSourcePortsText
        .split('\n')
        .map((line) => line.trim())
        .filter((line) => line.length > 0)
        .map((line) => Number(line)),
    };

    try {
      const result = await firstValueFrom(
        this.outboundQueryApi.apiOutboundQuerySettingsPatch({ body }),
      );
      this.notification.success(result.note ?? 'Outbound query settings saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save outbound query settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.outboundQueryApi.apiOutboundQuerySettingsGet());
      this.form.patchValue({
        enableSourcePortPool: settings.enableSourcePortPool ?? true,
        poolSizePerAddressFamily: settings.poolSizePerAddressFamily ?? 128,
        excludedSourcePortsText: (settings.excludedSourcePorts ?? []).join('\n'),
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load outbound query settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
