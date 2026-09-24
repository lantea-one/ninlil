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
 * This component reads and writes this host's own `ZoneTransferOptions` (`GET`/`PATCH
 * /api/zone-transfer-settings`) -- how many resource records a single AXFR/IXFR response message
 * this server serves out may carry before starting a new one. No secret material is involved, so
 * this is a plain read/PATCH-with-current-value round trip -- built from
 * {@link ZoneSigningSettingsComponent}'s own reference pattern.
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

  /** This method PATCHes every field this form holds, then surfaces the backend's own hot-reloaded note. */
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
