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
import { ZoneTransferSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "Zone Transfer" settings page (`/settings/zone-transfer`). It edits
 * `ZoneTransferOptions` through `GET` and `PATCH /api/zone-transfer-settings`
 * (`zonetransfersettings:read`, `zonetransfersettings:write`): how many records one AXFR or IXFR
 * response message this server sends may carry before it starts another. Changes are
 * hot-reloaded.
 */
@Component({
  selector: 'app-zone-transfer-settings',
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
  templateUrl: './zone-transfer-settings.component.html',
  styleUrl: './zone-transfer-settings.component.scss',
})
export class ZoneTransferSettingsComponent implements OnInit {
  private readonly zoneTransferApi = inject(ZoneTransferSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    recordsPerMessage: [100, [Validators.required, Validators.min(1)]],
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
        this.zoneTransferApi.apiZoneTransferSettingsPatch({ body: this.form.getRawValue() }),
      );
      this.notification.success(result.note ?? 'Zone transfer settings saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save zone transfer settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.zoneTransferApi.apiZoneTransferSettingsGet());
      this.form.patchValue({ recordsPerMessage: settings.recordsPerMessage ?? 100 });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load zone transfer settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
