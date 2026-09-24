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
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { ZoneSigningSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component reads and writes this host's own `ZoneSigningOptions` (`GET`/`PATCH
 * /api/zone-signing-settings`) -- the DNSSEC signature validity/inception/refresh timing
 * `ZoneSigningService`/`DnssecResigningBackgroundService` both consult. No secret material is
 * involved, so every field is a plain read/PATCH-with-current-value round trip -- built from
 * {@link RateLimitingSettingsComponent}'s own reference pattern for its duration-string fields.
 */
@Component({
  selector: 'app-zone-signing-settings',
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
    SpinnerComponent,
  ],
  templateUrl: './zone-signing-settings.component.html',
  styleUrl: './zone-signing-settings.component.scss',
})
export class ZoneSigningSettingsComponent implements OnInit {
  private readonly zoneSigningApi = inject(ZoneSigningSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    signatureValidityPeriod: ['30.00:00:00', [Validators.required]],
    inceptionOffset: ['01:00:00', [Validators.required]],
    refreshBeforeExpiration: ['5.00:00:00', [Validators.required]],
    pollInterval: ['06:00:00', [Validators.required]],
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
        this.zoneSigningApi.apiZoneSigningSettingsPatch({ body: this.form.getRawValue() }),
      );
      this.notification.success(result.note ?? 'Zone signing settings saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save zone signing settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.zoneSigningApi.apiZoneSigningSettingsGet());
      this.form.patchValue({
        signatureValidityPeriod: settings.signatureValidityPeriod ?? '30.00:00:00',
        inceptionOffset: settings.inceptionOffset ?? '01:00:00',
        refreshBeforeExpiration: settings.refreshBeforeExpiration ?? '5.00:00:00',
        pollInterval: settings.pollInterval ?? '06:00:00',
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load zone signing settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
