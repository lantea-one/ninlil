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
 * This component is the "Outbound Query Hardening" settings page (`/settings/outbound-query`). It
 * edits `OutboundQueryOptions`, the UDP source-port pool used for the server's recursive and
 * forwarded queries, through `GET` and `PATCH /api/outbound-query-settings`
 * (`outboundquerysettings:read`, `outboundquerysettings:write`). Every field is hot-reloaded.
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
    enableSourcePortPool: [false],
    poolSizePerAddressFamily: [128, [Validators.required, Validators.min(1)]],
    excludedSourcePortsText: [''],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes every field (excluded ports one per line), then shows the backend's note. */
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
        enableSourcePortPool: settings.enableSourcePortPool ?? false,
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
