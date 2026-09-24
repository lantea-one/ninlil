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
import { DnssecKeyRolloverSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';

/**
 * This component reads and writes this host's own `DnssecKeyRolloverOptions` (`GET`/`PATCH
 * /api/dnssec-key-rollover-settings`) -- the RFC 6781 §4.1.1.1 (Pre-Publish, ZSK)/§4.1.2
 * (Double-Signature, KSK) key-rollover timing `DnssecKeyRolloverBackgroundService` walks every
 * locally signed zone's own keys against. `externalParentDsTtl` in particular is a real,
 * operator-tunable value: it stands in for a real external registrar's own DS record TTL, which
 * this project has no live way to observe. No secret material is involved, so every field is a
 * plain read/PATCH-with-current-value round trip -- built from
 * {@link ZoneSigningSettingsComponent}'s own reference pattern.
 */
@Component({
  selector: 'app-dnssec-key-rollover-settings',
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
  templateUrl: './dnssec-key-rollover-settings.component.html',
  styleUrl: './dnssec-key-rollover-settings.component.scss',
})
export class DnssecKeyRolloverSettingsComponent implements OnInit {
  private readonly rolloverApi = inject(DnssecKeyRolloverSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    propagationDelay: ['01:00:00', [Validators.required]],
    externalParentDsTtl: ['1.00:00:00', [Validators.required]],
    pollInterval: ['00:15:00', [Validators.required]],
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
        this.rolloverApi.apiDnssecKeyRolloverSettingsPatch({ body: this.form.getRawValue() }),
      );
      this.notification.success(result.note ?? 'DNSSEC key rollover settings saved.');
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not save DNSSEC key rollover settings.'),
      );
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.rolloverApi.apiDnssecKeyRolloverSettingsGet());
      this.form.patchValue({
        propagationDelay: settings.propagationDelay ?? '01:00:00',
        externalParentDsTtl: settings.externalParentDsTtl ?? '1.00:00:00',
        pollInterval: settings.pollInterval ?? '00:15:00',
      });
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not load DNSSEC key rollover settings.'),
      );
    } finally {
      this.loading.set(false);
    }
  }
}
