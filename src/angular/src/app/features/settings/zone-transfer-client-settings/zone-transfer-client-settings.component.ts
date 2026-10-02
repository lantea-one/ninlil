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
import { ZoneTransferClientSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "Zone Transfer Client" settings page (`/settings/zone-transfer-client`).
 * It edits `ZoneTransferClientOptions` through `GET` and `PATCH /api/zone-transfer-client-settings`
 * (`zonetransferclientsettings:read`, `zonetransferclientsettings:write`): the primary's port and
 * the timeouts used when this server, as a secondary, pulls an AXFR or IXFR. Changes are
 * hot-reloaded.
 */
@Component({
  selector: 'app-zone-transfer-client-settings',
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
  templateUrl: './zone-transfer-client-settings.component.html',
  styleUrl: './zone-transfer-client-settings.component.scss',
})
export class ZoneTransferClientSettingsComponent implements OnInit {
  private readonly zoneTransferClientApi = inject(ZoneTransferClientSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    primaryPort: [53, [Validators.required, Validators.min(1), Validators.max(65535)]],
    connectTimeout: ['00:00:05', [Validators.required]],
    readTimeout: ['00:00:10', [Validators.required]],
    soaQueryTimeout: ['00:00:03', [Validators.required]],
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
        this.zoneTransferClientApi.apiZoneTransferClientSettingsPatch({
          body: this.form.getRawValue(),
        }),
      );
      this.notification.success(result.note ?? 'Zone transfer client settings saved.');
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not save zone transfer client settings.'),
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
        this.zoneTransferClientApi.apiZoneTransferClientSettingsGet(),
      );
      this.form.patchValue({
        primaryPort: settings.primaryPort ?? 53,
        connectTimeout: settings.connectTimeout ?? '00:00:05',
        readTimeout: settings.readTimeout ?? '00:00:10',
        soaQueryTimeout: settings.soaQueryTimeout ?? '00:00:03',
      });
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not load zone transfer client settings.'),
      );
    } finally {
      this.loading.set(false);
    }
  }
}
